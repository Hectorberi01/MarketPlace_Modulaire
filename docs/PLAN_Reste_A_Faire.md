# Plan — ce qu'il reste à faire

> Établi le 8 septembre 2026, à la fin des lots 1 à 4 du chantier portefeuille et du
> socle du service de dispatch. Deux bases de code : le monolithe `marketPlace` et le
> service `Services/HbaExpress.Dispatch`.

---

## 0. L'état réel, sans arrondi

**Branche `wallet` : 9 commits au-dessus de `develop`, rien de mergé.** C'est le premier
risque du projet, avant tout problème technique : plus la branche vieillit, plus la
réintégration devient un événement à part entière plutôt qu'une formalité.

| Chantier | État |
|---|---|
| Lot 1 — portefeuille unifié + réconciliation | fini, testé |
| Lot 2 — cagnotte client | fini, testé |
| Lot 3 — dépense au checkout | fini, testé |
| Lot 4 — coursier (identité + paiement) | fini côté marketplace |
| Service dispatch — domaine + persistance | fini, testé (verrou de concurrence prouvé) |
| Service dispatch — couche application | faite ; **son entrée HTTP n'existe pas** |
| Service dispatch — balayage de redispatch | écrit ; **aucun hôte pour l'exécuter** |
| Service dispatch — hub temps réel | pas commencé |

**Le service de dispatch ne peut pas démarrer.** Il n'a ni `Program.cs`, ni injection de
dépendances, ni port HTTP. Il compile, il se teste, il ne tourne pas. Tant que c'est vrai,
tout le reste du service est du code mort — y compris l'événement `OrderReadyForDelivery`
que la marketplace publie déjà et que personne ne consomme.

---

## 1. Merger `wallet` — avant d'écrire une ligne de plus

Neuf commits, deux mois de décisions, quatre lots. Chaque jour de retard ajoute du conflit
potentiel et éloigne le moment où ce travail sert à quelqu'un.

- Rebaser ou merger sur `develop`, lancer la suite complète.
- Déployer en staging (Hetzner) et laisser tourner quelques jours avec du trafic réel.
- **Vérifier `GET /admin/wallet/reconciliation`** : il doit répondre 200. Un 409 en staging
  signifie qu'un chemin d'écriture bouge un solde sans écrire au grand livre — à corriger
  avant la production, jamais après.

**Ne pas déployer en production tout de suite** : voir §5, les décisions non techniques.

---

## 2. Rendre le service de dispatch exécutable

C'est le seul bloc qui transforme du code en système. Rien d'autre ne devrait passer devant.

### 2.1 L'hôte (`HbaExpress.Dispatch.Api`)

Projet ASP.NET minimal : `Program.cs`, DI (`DispatchDbContext`, repositories, services),
application des migrations au démarrage, endpoint de santé.

### 2.2 L'entrée venant de la marketplace

Webhook signé HMAC, fermé par défaut — même schéma que `PartnerServiceSignature` côté
marketplace, secret distinct.

- `POST /events/order-ready-for-delivery` → crée la course et lance la première vague.
- `POST /events/order-cancelled` → annule la course si elle n'est pas livrée.
- `POST /events/courier-standing-changed` → recopie le droit de travailler (activation,
  suspension). **Sans lui, aucun coursier n'existe côté dispatch** : c'est par là qu'ils
  entrent dans le service.

### 2.3 Les routes coursier

`POST /me/online`, `/me/offline`, `PUT /me/position`, `PUT /me/service-area`,
`GET /me/offers`, `POST /jobs/{id}/accept`, `POST /jobs/{id}/decline`, et la progression
(`/pickup`, `/transit`, `/delivered`, `/failed`).

**Point de vigilance** : traduire `DbUpdateConcurrencyException` en réponse 409 « course
déjà prise ». Sans cette traduction, le coursier perdant reçoit une erreur 500 sur le
chemin le plus fréquent du service.

### 2.4 Le balayage en tâche de fond

`BackgroundService` appelant `RedispatchService.SweepAsync` toutes les 15 secondes.

**Un seul hôte doit l'exécuter.** Le monolithe a payé cette leçon trois fois (outbox,
réconciliation des retraits, expiration des remises) : poser la même condition dès le
départ ici.

### 2.5 Le transport marketplace → dispatch

Les événements sont dans l'outbox de la marketplace et **rien ne les livre**. Sans ce
morceau, tout ce qui précède reste inerte.

Le plus simple qui marche : un `IIntegrationEventHandler` côté marketplace qui appelle le
webhook du service en HTTP signé. Ce n'est pas un bus, mais l'outbox garantit déjà la
persistance et le réessai — l'essentiel est là.

### 2.6 Les événements sortants du dispatch

`DeliveryAccepted`, `DeliveryPickedUp`, `DeliveryInTransit`, `DeliveryDelivered`,
`DeliveryFailed`, `DeliveryExpired` → vers la marketplace, avec outbox côté dispatch.

C'est ce qui referme la boucle : `DeliveryDelivered` doit finir par créditer le coursier
via `POST /api/delivery/courses/completed`, déjà en place et signé.

**À ce stade, la chaîne complète fonctionne** : commande prête → course → offre →
acceptation → livraison → coursier payé.

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

Par ordre de gravité, telle que je la vois.

### 4.1 `SKIP LOCKED` sur l'outbox — plafond dur

L'API **ne peut pas être répliquée** : deux instances liraient les mêmes messages et les
dispatcheraient deux fois. Chaque gain vendeur crédité en double.

Ce n'est pas une opinion de performance, c'est une contrainte de déploiement. Elle bloque
toute montée en charge, et elle vaut maintenant aussi pour les trois tâches de fond.

### 4.2 Audit des groupes de routes de l'API

Le trou de `/api/shipments` — n'importe quel compte authentifié pouvait libérer le séquestre
de n'importe quel vendeur — venait d'un `MapGroup(...).RequireAuthorization()` sans contrôle
de propriété dans les handlers. **Onze groupes utilisent ce motif.** Les autres sont
*a priori* légitimes (ressources appartenant à l'appelant, résolu depuis le jeton), mais
aucun n'a été vérifié un par un.

Une session. C'est le même motif qui a produit une escalade réelle.

### 4.3 Plafond sur la rémunération d'une course

L'appelant de `POST /api/delivery/courses/completed` décide combien sort du portefeuille de
la plateforme. La signature protège d'un inconnu, pas d'un bug de tarification chez le
partenaire ni d'un service compromis.

À poser en même temps que la règle de tarif (§5.3).

### 4.4 Alerte sur les lettres mortes de l'outbox

Elles sont journalisées en `Critical` et **personne n'est réveillé**. Une règle Alertmanager,
une demi-journée.

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
| 1 | Merger `wallet`, déployer en staging | Neuf commits qui vieillissent |
| 2 | Instruire le point BCEAO (§5.1) | Peut invalider le lot 2 — à lancer en parallèle, ça prend du temps hors code |
| 3 | Hôte du dispatch (§2.1 → 2.4) | Rien ne tourne sans |
| 4 | Transport et boucle de retour (§2.5, 2.6) | Referme la chaîne de bout en bout |
| 5 | `SKIP LOCKED` (§4.1) | Plafond dur, à lever avant la charge |
| 6 | Audit des routes (§4.2) | Même motif qu'une escalade déjà trouvée |
| 7 | Hub temps réel (§3) | Accélère ce qui marche déjà |
| 8 | Repli en cascade / 3PL | Le plus gros bloc restant du CDC |

Les points 1 et 2 se mènent en parallèle : l'un est du code, l'autre des démarches.

---

## 7. Ce qui n'est volontairement pas dans ce plan

- **L'application coursier (Flutter)** — client, hors du périmètre backend.
- **Le repli en cascade vers un 3PL** — bloqué par une question factuelle du CDC : existe-t-il
  un prestataire exposant une API exploitable dans la zone cible ? À vérifier avant de
  concevoir quoi que ce soit.
- **La preuve de livraison** (photo, code à 4 chiffres, signature) — décision produit ouverte
  au CDC §12.
