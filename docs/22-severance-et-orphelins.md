# 22 — Clés étrangères obligatoires : ce que j'ai cru, et ce que la base a dit

> **Date :** 14 juillet 2026.
>
> **Ce document a été entièrement réécrit après confrontation aux données réelles.** La
> première version décrivait une « fuite de stockage sur le chemin le plus chaud de la
> plateforme ». **Cette fuite n'existe pas.** Je l'avais déduite d'une règle EF Core
> appliquée de mémoire, sans vérifier qu'elle valait dans cette configuration.
>
> Ce qui suit raconte les deux erreurs, ce que la base a réellement révélé, et pourquoi le
> correctif reste justifié — pour une raison plus modeste, mais plus solide.

---

## Ce que j'ai affirmé, et pourquoi c'était faux

Neuf relations parent-enfant n'avaient pas `.IsRequired()`. J'en ai conclu qu'EF les traitait
comme **optionnelles** et que, lors du retrait d'un enfant, il « sèvrerait » la ligne
(`UPDATE enfant SET FK = NULL`) au lieu de la supprimer — laissant un orphelin à chaque fois.

### Erreur n°1 — j'ai désigné le mauvais coupable

Dans la [doc 20](20-derive-des-migrations-et-cles-etrangeres.md), j'ai écrit que
`Ordering → Lines` était **« le plus urgent »** : *« des lignes de commande orphelines, ce
sont des ventes qui n'appartiennent à personne »*.

**Rien ne supprime jamais une commande, ni ne retire une ligne d'une commande.** Ce chemin
n'existe pas. J'avais raisonné par gravité théorique au lieu de regarder où le code supprime
effectivement des enfants.

### Erreur n°2 — le mécanisme lui-même n'opère pas

Les neuf relations portaient **déjà** `.OnDelete(DeleteBehavior.Cascade)`, explicitement.

Or en EF Core, ce `DeleteBehavior` ne gouverne pas seulement la suppression du parent : il
gouverne aussi le sort des **orphelins**. Avec `Cascade`, un enfant retiré de sa collection
est **supprimé**. La sévérance est le comportement de `ClientSetNull` — le *défaut* d'une
relation optionnelle, mais ici il était explicitement remplacé.

**Je n'ai pas vérifié ce point. Je l'ai supposé.**

---

## Ce que la base a dit

| Table | lignes | orphelins |
|---|---:|---:|
| `ordering.orders` | 10 | — |
| `ordering.order_lines` | 12 | **0** |
| `inventory.inventory_items` | 8 | — |
| `inventory.stock_reservations` | **0** | **0** |
| `messaging.conversation_messages` | 7 | 0 |
| `messaging.message_reactions` | 4 | **1** |
| `identity.users` | 6 | — |

**Dix commandes ont été passées et confirmées. `stock_reservations` est à zéro — total ET
orphelins.** Les réservations ont donc été créées, puis **proprement supprimées**. Si la
sévérance opérait, il y aurait là une douzaine de lignes mortes. Il n'y en a aucune.

La « fuite sur le chemin le plus chaud » que j'annonçais est réfutée par les données.

---

## L'anomalie que je n'explique pas

Une ligne, une seule, dans `message_reactions` :

```
Id        4192d7f4-…    UserId    8afd929b-…    Emoji  ❤️
CreatedAtUtc  2026-07-11 16:25    MessageId  NULL
```

Un utilisateur réel a posé un cœur, et cette ligne n'est rattachée à aucun message.

**Je ne sais pas d'où elle vient.** La configuration des réactions est identique à celle des
réservations (`OnDelete(Cascade)`), la collection est chargée avant modification, et le
retrait passe par le même mécanisme. Une version antérieure de la configuration ? Une
manipulation manuelle pendant les tests du 11 juillet ? Un chemin de code que je n'ai pas
trouvé ?

Je pourrais inventer une explication plausible. Je préfère écrire que je n'en ai pas.

---

## Pourquoi le correctif reste justifié — pour une autre raison

`.IsRequired()` a été posé sur les neuf relations. **Pas pour réparer une fuite : elle
n'existe pas.** Pour trois raisons plus modestes, et plus sûres.

**1. Le modèle disait quelque chose de faux.** Ces clés étrangères sont réellement
obligatoires — une réservation sans article, une réaction sans message, une ligne sans
commande n'ont aucun sens métier. Le modèle les déclarait facultatives. Un modèle qui ment
finit toujours par produire du code qui se trompe.

**2. La base ne garantissait rien.** Les colonnes étaient `NULL`-ables. **Et cette unique
ligne orpheline le prouve : quelque chose, quelque part, a pu en créer une.** Que ce soit du
code d'aujourd'hui, d'hier, ou une opération manuelle — une contrainte `NOT NULL` l'aurait
refusée. C'est la seule protection qui ne dépende ni de la configuration EF du moment, ni de
la vigilance du prochain développeur.

**3. Le comportement dépendait d'un réglage fragile.** Retirer `OnDelete(Cascade)` — un
geste anodin en apparence — ferait *réellement* basculer ces neuf relations en sévérance, et
la fuite que j'ai décrite à tort deviendrait vraie. Avec `IsRequired()` **et** `NOT NULL`,
ce scénario est impossible : EF ne peut plus sevrer une colonne que la base refuse de mettre
à `NULL`.

> **La leçon, et c'est la troisième fois sur ce sujet.** J'ai raisonné par principe — « pas
> d'`IsRequired()` donc relation optionnelle donc sévérance » — au lieu de vérifier la règle
> et de regarder les données. Le principe était juste dans l'abstrait, et faux ici, parce
> qu'une ligne de configuration que j'avais lue sans la voir le neutralisait.
>
> **Une déduction élégante ne remplace pas un `SELECT count(*)`.**

---

## Application

Une seule ligne à supprimer avant la migration :

```sql
DELETE FROM messaging.message_reactions WHERE "MessageId" IS NULL;
```

Puis les migrations, qui passent les neuf colonnes en `NOT NULL` :

```bash
dotnet ef migrations add MakeChildForeignKeysRequired -p src/Modules/Identity/Marketplace.Modules.Identity.Infrastructure
dotnet ef migrations add MakeChildForeignKeysRequired -p src/Modules/Inventory/Marketplace.Modules.Inventory.Infrastructure
dotnet ef migrations add MakeChildForeignKeysRequired -p src/Modules/Messaging/Marketplace.Modules.Messaging.Infrastructure
dotnet ef migrations add MakeChildForeignKeysRequired -p src/Modules/Ordering/Marketplace.Modules.Ordering.Infrastructure
dotnet ef migrations add MakeChildForeignKeysRequired -p src/Modules/Settlement/Marketplace.Modules.Settlement.Infrastructure
```

Chacune ne doit contenir que des `AlterColumn` (nullable → `NOT NULL`). **Relisez-les** — la
[doc 20](20-derive-des-migrations-et-cles-etrangeres.md) rappelle qu'une migration générée
n'est pas une vérité, mais un diff entre deux croyances.

Le refus de Postgres, s'il reste un orphelin, n'est pas un obstacle : c'est exactement la
garantie qu'on vient d'installer, qui fonctionne pour la première fois.
