# 19 — A9 : l'outbox rejouait les messages empoisonnés pour l'éternité

> **Date :** 14 juillet 2026. Point de départ : **A9**, classé « important » — *« l'outbox
> n'a ni plafond de tentatives ni dead-letter »*.
>
> Ce point était devenu **le plus urgent de la liste, et par ma faute** : les deux correctifs
> précédents (canal e-mail, décompte des coupons) s'appuient tous deux sur le rejeu de
> l'outbox. J'avais transformé une lacune théorique en risque opérationnel.

---

## Ce que le code faisait

`OutboxProcessor`, en cas d'échec de publication :

```csharp
catch (Exception ex)
{
    message.Error = ex.Message;                       // on note l'erreur
    _logger.LogError(ex, "Échec de publication…");    // on la journalise
    // … et c'est tout. ProcessedOnUtc reste null.
}
```

Le message revenait au tour suivant. Et au suivant. **Toutes les 5 secondes, sans limite,
sans délai, sans issue.** Une adresse e-mail invalide, un domaine non vérifié chez Resend,
un type d'événement renommé, un JSON devenu illisible — et la boucle tournait jusqu'à la
fin des temps.

## Le vrai danger n'était pas le bruit

C'était le **blocage de tête de file**.

```csharp
.Where(m => m.ProcessedOnUtc == null)
.OrderBy(m => m.OccurredOnUtc)
.Take(50)                              // ← le lot
```

Le lot est trié par date et plafonné à 50. **Chaque message empoisonné confisquait une
place, définitivement.** À 50 messages morts, l'outbox du module se figeait : plus une seule
commande confirmée, plus un seul vendeur crédité, plus une seule notification. Et rien ne le
signalait, sinon un log qui se répétait.

Une panne qui commence par un e-mail refusé, et finit par une plateforme muette.

---

## Le correctif, en quatre pièces

### 1. Backoff exponentiel — c'est lui qui débloque la file

`OutboxMessage.NextAttemptAtUtc`. Un message en échec **sort du lot** le temps de sa
temporisation ; les messages sains passent devant. Le blocage de tête de file disparaît.

Progression : 10 s, 20 s, 40 s, 80 s… plafonnée à 30 min, avec une **gigue de ±20 %**.

> **La gigue n'est pas cosmétique.** Sans elle, tous les messages tombés pendant la même
> panne — souvent des centaines — reviendraient *exactement* au même instant, et frapperaient
> le service qui vient à peine de se relever. On l'achèverait, et le cycle recommencerait.

### 2. Plafond — 10 tentatives, ~2 h

Deux erreurs symétriques à éviter :

| Erreur | Conséquence |
|---|---|
| Rejouer sans fin (l'état d'origine) | La file se bloque. |
| Abandonner trop vite | Une panne passagère (Resend indisponible 3 min) enterre des messages **sains** — un vendeur jamais payé. |

Le backoff exponentiel tranche : on insiste beaucoup au début, on espace ensuite, et on
n'abandonne qu'après ~2 h — bien au-delà d'un incident ordinaire.

### 3. Lettre morte visible — sinon le remède serait pire que le mal

Un plafond **sans** file de lettres mortes aurait enterré ces messages **en silence**. On
aurait remplacé une boucle bruyante par une perte invisible. **Ce serait pire** : au moins,
la boucle finissait par se voir.

Trois canaux, donc :

- `LogCritical` — et non `LogError` : ce n'est pas « une tentative a échoué », c'est
  « cet événement métier ne se produira **jamais** ».
- **Métrique** `outbox_dead_letters_total{module, event_type}` → alerte Prometheus
  `OutboxDeadLetter`, **severity: critical, seuil zéro**. Un seul suffit à réveiller
  quelqu'un.
- **Endpoint admin** `GET /admin/outbox/dead-letters` (les 25 modules), avec `replay` et
  `discard`.

> Une lettre morte n'est pas un incident technique. C'est un e-mail de réinitialisation
> jamais envoyé — donc un utilisateur enfermé dehors. Un gain vendeur jamais crédité — donc
> de l'argent dû qui n'existe nulle part. Un stock jamais libéré — donc un article
> invendable. **Cette liste doit rester vide.**

### 4. Index partiels — la requête doit rester à coût constant

```csharp
builder.HasIndex(m => new { m.NextAttemptAtUtc, m.OccurredOnUtc })
    .HasFilter("\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");
```

C'est **la** requête du système : toutes les 5 s, sur 25 tables, indéfiniment. L'index est
**partiel** : il ne contient que les messages en attente — une poignée. Les millions de
lignes traitées, qui s'accumulent sans jamais sortir de la table, n'y entrent jamais.

---

## ⚠️ Une contrainte de déploiement, découverte au passage

`OutboxProcessor` ne pose **aucun** `SELECT … FOR UPDATE SKIP LOCKED`.

Deux processeurs concurrents liraient donc les **mêmes** messages et les dispatcheraient
**deux fois**. Chaque gain vendeur crédité deux fois.

C'est aujourd'hui contenu : les 4 BFF posent `OUTBOX_ENABLED=false`, seule l'API draine.
Mais cela signifie que **l'API ne peut PAS être mise à l'échelle horizontalement** en
l'état. Le commentaire du code ne mentionnait que le risque de saturation des connexions
Postgres ; le risque de double dispatch — bien plus grave — n'était écrit nulle part. Il
l'est maintenant, dans `OutboxRegistration`.

**Avant de scaler l'API : implémenter le verrou de ligne.** Ce n'est pas une opinion, c'est
une condition.

---

## Migration — 25 fois

La table `outbox_messages` existe dans **les 25 schémas**.

```bash
./scripts/add-outbox-retry-migrations.sh
```

Trois colonnes ajoutées (`AttemptCount` défaut 0, `NextAttemptAtUtc`, `DeadLetteredOnUtc`)
et deux index partiels. **Aucune reprise de données** : les lignes existantes démarrent à
0 tentative, sans temporisation ni lettre morte — exactement le comportement d'avant.

---

## Ce que ce point enseigne

**Supprimer un symptôme bruyant sans le remplacer par un signal, c'est fabriquer une panne
silencieuse.** La boucle infinie était intenable — mais elle *se voyait*. En posant un
plafond, on l'a fait taire. Sans l'alerte et la file d'administration, on aurait rendu le
système plus « propre » et strictement plus dangereux.

C'est la même leçon que le verrou `xmin` inerte sur `InventoryItem`, et que le rate limiter
placé avant l'authentification : **un mécanisme peut être présent, documenté, et ne rien
faire.** Il faut toujours vérifier l'exécution, jamais l'intention.
