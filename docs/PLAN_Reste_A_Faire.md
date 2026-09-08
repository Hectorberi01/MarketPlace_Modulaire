# Plan — ce qu'il reste à faire

> Établi le 8 septembre 2026. **Troisième révision**, après le merge dans `develop`, la
> construction complète de la chaîne de livraison, et l'audit des groupes de routes de
> l'API. Deux bases de code : le monolithe `marketPlace` et le service
> `Services/HbaExpress.Dispatch`.

---

## 0. L'état réel, sans arrondi

`develop`, `develop_staging` et `wallet` pointent sur le même commit. La chaîne complète
— commande prête → course → offre → acceptation → livraison → coursier payé — **existe en
code des deux côtés et n'a jamais été exécutée une seule fois.**

| Chantier | État |
|---|---|
| Lots 1 à 4 — portefeuille, cagnotte, checkout, coursier | fini, testé, mergé |
| Service dispatch — domaine, application, persistance | fini, testé |
| Service dispatch — hôte HTTP, routes internes et coursier | fini |
| Service dispatch — balayage de redispatch | fini |
| Transport marketplace → dispatch | fini |
| Annonce de livraison → paiement du coursier | fini |
| Plafond de rémunération d'une course | fini (relatif + absolu) |
| Audit des groupes de routes de l'API | fini — 3 failles corrigées |
| **Passage de bout en bout** | **script écrit, jamais lancé** |
| `SKIP LOCKED` | pas commencé — concerne maintenant 3 balayages |
| Observabilité du service de dispatch | **inexistante** |
| Déploiement du service de dispatch | **inexistant** |
| Hub temps réel | pas commencé |

### Ce qui a été trouvé en construisant, et qu'aucun test ne voyait

- **Le coursier avait deux identifiants et les deux services n'utilisaient pas le même.**
  Le dispatch enregistrait le `CourierId` (celui qui fait payer) et lisait le `UserId`
  dans le jeton. Toutes les routes de l'application coursier auraient répondu 404 : un
  coursier à qui l'on offre des courses et à qui l'on refuse de les prendre. Trouvé en
  préparant le passage de bout en bout, avant qu'une ligne ne tourne.
- **`POST /api/returns/{id}/refund` était ouvert à tout compte authentifié**, sans aucun
  contrôle de propriété, et créditait réellement la cagnotte. Même motif que
  `POST /api/shipments/{id}/deliver`. Deux autres failles à côté : lecture du fil complet
  d'un litige d'autrui (photos de preuves comprises) et initiation d'un paiement sur la
  commande d'un tiers, qui la rendait définitivement impayable.

---

## 1. Lancer le passage de bout en bout — avant tout le reste

`scripts/e2e_dispatch.py`, quatre hôtes, aucune dépendance. Il enchaîne le parcours
complet et s'arrête net au premier écart, requête et corps à l'appui.

C'est le seul point de cette liste qui puisse encore infirmer tout le reste. Les tests
couvrent les morceaux ; les jointures — un champ renommé, un identifiant qui n'est pas
celui qu'on croit, un secret croisé à l'envers — n'apparaissent qu'à l'exécution. La
seule qu'on ait cherchée en a livré une.

---

## 2. Ce qui empêche encore de déployer

### 2.1 Le service de dispatch n'est nulle part dans le déploiement

Ni `Dockerfile`, ni service dans les trois `docker-compose`, ni règle Traefik, ni base
`dispatch` provisionnée. Il tourne en développement et ne peut pas quitter la machine.

À faire : image, service compose (dev / staging / prod), base dédiée, route d'edge
restreinte — `/internal` ne doit **jamais** être joignable depuis Internet, seul le
réseau interne l'appelle ; `/api/courier` l'est, lui, pour l'application mobile.

### 2.2 Le service de dispatch est aveugle

Aucun Serilog, aucun OpenTelemetry, aucune métrique. Le monolithe a tout cela, et une
règle d'alerte `OutboxDeadLetter` qui réveille quelqu'un dès la première lettre morte.

Côté dispatch, un message sortant abandonné — donc **une course livrée que personne n'a
payée** — ne produit qu'un journal et une sonde de santé en « dégradé ». Personne ne
regarde une sonde. Il faut la métrique et la règle jumelle.

### 2.3 `SKIP LOCKED` — plafond dur, et il concerne trois balayages

Aucun des trois ne pose de verrou de ligne :

| Balayage | Ce que coûte une seconde instance |
|---|---|
| Outbox du monolithe | double dispatch de tous les événements — argent compris |
| Balayage de redispatch | deux vagues d'offres par course : le même coursier appelé deux fois |
| File sortante du dispatch | doublon absorbé par l'idempotence de la marketplace — bénin |

Tant que le premier n'est pas réglé, **l'API ne peut pas être répliquée**. Ce n'est pas
un problème aujourd'hui ; c'en sera un le jour où il faudra encaisser la charge, et ce
jour-là il sera trop tard pour le découvrir.

---

## 3. Le hub temps réel — après, pas avant

`IOfferNotifier` a été posé comme port exprès : la décision « qui gagne » ne dépend pas du
transport. Une offre existe en base dès sa création, et un coursier peut l'accepter par
simple appel HTTP même si le message poussé s'est perdu.

Le hub accélère ce qui marche déjà. Le construire avant l'hôte aurait donné un système dont
on ne peut rien affirmer tant que tout est branché.

Prévoir : reconnexion, registre des coursiers en ligne, et un repli sur le polling — les
réseaux mobiles béninois coupent.

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

| # | Bloc | Pourquoi maintenant |
|---|---|---|
| 1 | **Lancer le passage de bout en bout** (§1) | Peut infirmer tout le reste. Rien ne devrait passer devant |
| 2 | Instruire la BCEAO (§5.1) | Hors code, long, et peut invalider le lot 2 — à lancer en parallèle |
| 3 | Vérifier le contrôle de partie des litiges (§4.1) | Une heure, et c'est peut-être une fonction morte depuis le début |
| 4 | Déploiement du service de dispatch (§2.1) | Il ne peut pas quitter la machine |
| 5 | Observabilité + alerte du dispatch (§2.2) | Une course livrée non payée doit réveiller quelqu'un |
| 6 | `SKIP LOCKED` (§2.3) | Plafond dur, à lever avant d'en avoir besoin |
| 7 | Hub temps réel (§3) | Accélère ce qui marche déjà |
| 8 | Repli en cascade / 3PL | Le plus gros bloc restant du CDC |

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
