# Plan — ce qu'il reste à faire

> Établi le 8 septembre 2026. **Quatrième révision**, après la construction complète de la
> chaîne de livraison, l'audit des routes, `SKIP LOCKED`, l'observabilité, le déploiement
> et le hub temps réel. Deux bases de code : le monolithe `marketPlace` et le service
> `Services/HbaExpress.Dispatch`.

---

## 0. L'état réel, sans arrondi

**Tout ce qui était du code est écrit.** Ce qui reste tient en trois catégories, et deux
d'entre elles ne se règlent pas dans un éditeur : une vérification à faire tourner, des
décisions à prendre, et des dettes de moindre gravité.

| Chantier | État |
|---|---|
| Lots 1 à 4 — portefeuille, cagnotte, checkout, coursier | fini, mergé |
| Service dispatch — domaine, application, persistance, hôte, routes | fini |
| Transport marketplace → dispatch, et retour | fini |
| Plafond de rémunération d'une course | fini |
| Audit des groupes de routes — 3 failles corrigées | fini |
| `SKIP LOCKED` | fini sur l'outbox et le redispatch ; volontairement écarté sur la file sortante |
| Observabilité du dispatch + alertes | fini |
| Déploiement du dispatch (image, compose ×3, Traefik, base) | fini |
| Hub temps réel | fini |
| **Passage de bout en bout** | **script écrit, jamais lancé** |
| Décisions non techniques (§5) | aucune tranchée |

### Ce qui a été trouvé en construisant, et qu'aucun test ne voyait

- **Le coursier avait deux identifiants et les deux services n'utilisaient pas le même.**
  Toutes les routes de l'application coursier auraient répondu 404 : un coursier à qui
  l'on offre des courses et à qui l'on refuse de les prendre. Trouvé en préparant le
  passage de bout en bout, avant qu'une ligne ne tourne.
- **`POST /api/returns/{id}/refund` était ouvert à tout compte authentifié** et créditait
  réellement la cagnotte. Deux autres à côté : lecture du fil complet d'un litige
  d'autrui, et initiation d'un paiement sur la commande d'un tiers — qui la rendait
  définitivement impayable.

---

## 1. Lancer le passage de bout en bout — ce qui devrait passer devant tout

`scripts/e2e_dispatch.py`, quatre hôtes, aucune dépendance. Il enchaîne le parcours
complet et s'arrête net au premier écart, requête et corps à l'appui.

C'est le seul point de cette liste qui puisse encore infirmer le reste. Les tests
couvrent les morceaux ; les jointures — un champ renommé, un identifiant qui n'est pas
celui qu'on croit, un secret croisé à l'envers — n'apparaissent qu'à l'exécution. **La
seule qu'on ait cherchée en a livré une.**

Tant qu'il n'a pas tourné une fois, la phrase honnête reste : la chaîne complète existe
en code et n'a jamais été exécutée.

---

## 2. ~~Ce qui empêchait de déployer~~ — **fait**

### 2.1 Déploiement — fait

Image dans `build-and-push.sh`, service dans les trois composes, base séparée
(conteneur dédié en dev, `DISPATCH_CONNECTION` obligatoire ailleurs), routage Traefik.
`deploy-prod.sh` compile et teste désormais **les deux** solutions : le verrou de
compilation ne couvrait pas le service qui déclenche le paiement des coursiers.

**Seul `/api/courier` et `/hubs/courier` sont publiés. `/internal` n'a aucune route** —
et l'absence EST le contrôle : ces routes créent des courses et décident du droit de
travailler d'un coursier, leur authentification est une signature et non un jeton.

### 2.2 Observabilité — fait

Serilog, OpenTelemetry, compteurs métier, liveness/readiness. Deux compteurs portent une
alerte : `dispatch_outbound_dead_letters` (une course livrée que personne n'a payée,
seuil zéro) et `dispatch_jobs_expired` (un acheteur sans livreur, seuil de trois en dix
minutes). Les métriques passent par le collecteur OTel déjà scrapé — rien à ajouter dans
`prometheus.yml`.

### 2.3 `SKIP LOCKED` — fait, avec une exception assumée

| Balayage | État |
|---|---|
| Outbox du monolithe | verrouillé — **l'API est redevenue réplicable** |
| Balayage de redispatch | verrouillé |
| File sortante du dispatch | **délibérément non verrouillé** |

Le verrou ne tient que jusqu'au commit : le poser sur la file sortante obligerait à garder
une transaction ouverte pendant les appels HTTP du lot. Le doublon, lui, est absorbé —
la marketplace ne paie qu'une fois par identifiant de course. Le raisonnement est écrit
dans le dépôt avec sa condition d'invalidation.

**Restent non répliquables** : `DiscountExpiryService` et `WithdrawalReconciliationService`,
qui lisent encore sans réserver. `BACKGROUND_JOBS_ENABLED` reste donc nécessaire.

---

## 3. ~~Le hub temps réel~~ — **fait**

`/hubs/courier`, groupe dérivé du jeton, aucune méthode appelable par le client, backplane
Redis. Le backplane n'est pas du confort : depuis `SKIP LOCKED`, le service peut tourner
en plusieurs instances, et sans lui un coursier connecté à l'une ne recevrait jamais une
offre dispatchée par l'autre.

Ce qui reste ouvert de ce chapitre : **la reconnexion et le repli côté application
mobile**. Les réseaux mobiles béninois coupent, et c'est un travail de client, pas de
serveur — `GET /api/courier/offers` est le rattrapage prévu pour ça.

---

## 4. Dettes de la marketplace

Par ordre de gravité, telle que je la vois. `SKIP LOCKED` a déménagé en §2.3 : ce n'est
plus une dette, c'est un préalable au déploiement.

### 4.1 Le contrôle de partie des litiges est peut-être faux — **à vérifier**

`OpenDisputeCommandHandler` décide si un vendeur est partie à une commande par
`order.Lines.Any(l => l.SellerId == command.RaisedBy)`. Or `RaisedBy` est un identifiant
d'**utilisateur** et `SellerId` un identifiant de **boutique**.

Si ce sont deux Guid distincts — comme ils l'étaient pour le coursier — alors **aucun
vendeur ne peut ouvrir de litige**, et personne ne s'en est jamais aperçu : la condition
échoue en silence, l'erreur rendue est « vous n'êtes pas partie à cette commande », et on
la lit comme un refus légitime.

Une heure pour trancher. Si c'est confirmé, chercher le même motif ailleurs : c'est la
troisième fois qu'un identifiant est pris pour un autre dans ce dépôt.

### 4.2 ~~Audit des groupes de routes~~ — **fait**

Trois failles corrigées : cycle de vie des retours ouvert à tout compte authentifié (il
créditait la cagnotte), lecture du fil complet d'un litige d'autrui, initiation d'un
paiement sur la commande d'un tiers. Les neuf groupes sains repassés par
`MapAuthenticatedGroup`. Trois `MapGroup` nus subsistent, tous documentés : webhook
coursier, webhooks PSP, connexion.

### 4.3 ~~Plafond sur la rémunération d'une course~~ — **fait**

Relatif — jamais plus que les frais de livraison encaissés sur la commande, un invariant
qui s'adapte seul — et absolu, `Settlement:CourierPay:MaxAmount`, facultatif. La commande
refuse de payer si elle ne retrouve pas la commande : c'est le cas le plus douteux, pas
celui où l'on relâche le contrôle.

### 4.4 Alerte sur les lettres mortes — **à moitié faite**

La règle `OutboxDeadLetter` existe et réveille quelqu'un dès la première, côté monolithe.
La file sortante du service de dispatch n'a **ni métrique ni alerte** — seulement un
journal et une sonde de santé. Voir §2.2 : cela dépend de l'observabilité du service, qui
n'existe pas encore.

### 4.5 Dettes de moindre gravité

- **`ICommissionModuleApi` jamais appelé** : barème de commission unique dans Billing.
- **Remise vendeur en double** : `Offer.ApplyDiscount` et `Promotion(FundedBy.Seller)` font
  la même chose par deux chemins.
- **Audiences JWT séparées** entre les BFF.
- **Compte `Pending` du coursier inutilisé** : il existe dans la politique et vaudra toujours
  zéro. Soit on lui donne un séquestre avec son déclencheur de libération, soit on assume par
  écrit qu'il ne sert pas.

---

## 5. Décisions à prendre — ce ne sont pas des tâches de code

Ces quatre points peuvent invalider du travail déjà fait. Aucun ne se tranche dans un
éditeur.

### 5.1 Statut BCEAO de la monnaie électronique — **bloquant**

Détenir de l'argent de clients (la cagnotte) n'a pas le même régime réglementaire que le
transporter. C'est le seul point de cette liste qui peut remettre en cause **le lot 2 entier**.

À instruire avant toute mise en production de la cagnotte, pas après.

### 5.2 Expiration et plafond de la cagnotte

Sans expiration, la plateforme accumule une dette sans horizon. Sans plafond par client, elle
n'a aucune borne sur ce qu'elle peut devoir à une seule personne.

### 5.3 Qui fixe le prix d'une course

Le CDC laisse la question ouverte (§12). Aujourd'hui le montant vient du service de dispatch
et la marketplace ne le recalcule pas. Payer un coursier **débite le compte « frais de
livraison » de la plateforme** : si ce compte passe en négatif, c'est le signal que les
forfaits facturés aux acheteurs (1 500 / 3 000 F) ne couvrent pas les courses.

À surveiller dès les premières livraisons réelles — c'est une mesure, pas une estimation.

### 5.4 Retour sur commande payée en cagnotte

La contre-passation recrédite **tout** sur la cagnotte, y compris la part réglée en Mobile
Money. C'est défendable — le lot 2 a fait de la cagnotte le canal de remboursement par
défaut — mais c'est un choix qui n'a pas été arbitré.

---

## 6. Ordre proposé

Il ne reste plus de bloc de code à écrire. L'ordre est donc celui de la vérification et
des décisions.

| # | Bloc | Pourquoi maintenant |
|---|---|---|
| 1 | **Lancer le passage de bout en bout** (§1) | Peut infirmer tout le reste. Rien ne devrait passer devant |
| 2 | Instruire la BCEAO (§5.1) | Hors code, long, et peut invalider le lot 2 — à lancer le même jour |
| 3 | Vérifier le contrôle de partie des litiges (§4.1) | Une heure, et c'est peut-être une fonction morte depuis le début |
| 4 | Déployer en staging et laisser tourner | Vérifier `GET /admin/wallet/reconciliation` → 200, et les alertes du dispatch |
| 5 | Trancher §5.2, §5.3, §5.4 | Trois décisions produit, pas trois tâches |
| 6 | Dettes de moindre gravité (§4.5) | Aucune ne bloque, toutes finissent par coûter |
| 7 | Repli en cascade / 3PL | Le plus gros bloc restant du CDC — et bloqué par une question factuelle |

Les points 1, 2 et 3 se mènent en parallèle : un script à lancer, des démarches à
engager, une vérification à faire.

---

## 7. Ce qui n'est volontairement pas dans ce plan

- **L'application coursier (Flutter)** — client, hors du périmètre backend.
- **Le repli en cascade vers un 3PL** — bloqué par une question factuelle du CDC : existe-t-il
  un prestataire exposant une API exploitable dans la zone cible ? À vérifier avant de
  concevoir quoi que ce soit.
- **La preuve de livraison** (photo, code à 4 chiffres, signature) — décision produit ouverte
  au CDC §12.
