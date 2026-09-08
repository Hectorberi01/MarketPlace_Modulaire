# Plan — ce qu'il reste à faire

> Établi le 8 septembre 2026, à la fin des lots 1 à 4 du chantier portefeuille et du
> socle du service de dispatch. **Révisé le même jour**, après le merge dans
> `develop_staging` et la construction de l'hôte du service de dispatch.
> Deux bases de code : le monolithe `marketPlace` et le service
> `Services/HbaExpress.Dispatch`.

---

## 0. L'état réel, sans arrondi

**`wallet` est mergé dans `develop_staging`** — les deux branches pointent sur le même
commit. `develop`, en revanche, reste **10 commits en arrière** : les trois correctifs
d'argent (plafond de remboursement, `Withdrawal.Create` sans contrôle de solde,
`/api/shipments/{id}/deliver` ouvert à tout compte authentifié) ne sont pas encore sur la
branche d'intégration.

| Chantier | État |
|---|---|
| Lot 1 — portefeuille unifié + réconciliation | fini, testé |
| Lot 2 — cagnotte client | fini, testé |
| Lot 3 — dépense au checkout | fini, testé |
| Lot 4 — coursier (identité + paiement) | fini côté marketplace |
| Service dispatch — domaine + persistance | fini, testé (verrou de concurrence prouvé) |
| Service dispatch — couche application | finie |
| Service dispatch — hôte HTTP + routes | **écrit, pas encore compilé** |
| Service dispatch — balayage de redispatch | branché sur l'hôte |
| Service dispatch — annonce de livraison à la marketplace | **manquante** |
| Marketplace → dispatch : livraison des événements | **manquante** |
| Service dispatch — hub temps réel | pas commencé |

**Le service démarre désormais** — enfin, il le devrait : le code de l'hôte est écrit et
n'a pas encore passé un `dotnet build`. C'est la première chose à vérifier.

**Il reste deux ruptures dans la chaîne, et ce sont les deux mêmes : du silence.**

1. **La marketplace n'envoie rien.** `OrderReadyForDeliveryIntegrationEvent` part bien dans
   l'outbox, qui le draine et le dispatche en mémoire — vers **zéro handler**. Le message
   est alors marqué traité. Pas d'erreur, pas de lettre morte, pas de journal : une
   commande prête à livrer disparaît sans laisser de trace, et le dispatch tourne à vide.
2. **La livraison ne paie personne.** `POST /jobs/{id}/delivered` ferme la course et libère
   le coursier ; l'appel à `POST /api/delivery/courses/completed` n'existe pas. Un
   avertissement est journalisé à chaque livraison — c'est un pansement, pas une solution.

---

## 1. Vérifier ce qui vient d'être écrit, et rattraper `develop`

- `dotnet build` et `dotnet test` sur `Services/HbaExpress.Dispatch/HbaExpress.Dispatch.sln`.
- Lancer le service en développement, ouvrir Swagger, appeler `/health`.
- **Reporter `develop_staging` sur `develop`.** Tant que les correctifs d'argent ne sont
  que sur staging, toute branche partant de `develop` repart avec les trous.
- Déployer en staging et laisser tourner quelques jours avec du trafic réel.
- **Vérifier `GET /admin/wallet/reconciliation`** : il doit répondre 200. Un 409 en staging
  signifie qu'un chemin d'écriture bouge un solde sans écrire au grand livre — à corriger
  avant la production, jamais après.

**Ne pas déployer en production tout de suite** : voir §5, les décisions non techniques.

---

## 2. Refermer la chaîne

Le service écoute ; personne ne lui parle, et il ne répond à personne. Ces deux morceaux
sont ce qui sépare « un service qui démarre » de « des courses livrées ».

### 2.1 L'hôte — **fait**

`HbaExpress.Dispatch.Api` : `Program.cs`, DI, JWT (clé partagée avec la marketplace),
politique d'autorisation par repli, `ProblemDetails`, migrations au démarrage, `/health`,
Swagger en développement.

### 2.2 L'entrée venant de la marketplace — **fait**

Signature HMAC du corps brut, fermée par défaut, secret distinct de celui du sens inverse.

- `POST /internal/orders/ready-for-delivery` → crée la course et lance la première vague.
- `POST /internal/orders/cancelled` → annule la course et libère le coursier.
- `POST /internal/couriers/standing-changed` → recopie le droit de travailler. C'est par là
  qu'un coursier entre dans le service ; il n'y a pas d'inscription séparée.

### 2.3 Les routes coursier — **fait**

`/api/courier` : en ligne, hors ligne, position, zone, offres, course en cours,
accepter, refuser, ramassages, incident, transit, livraison, échec.

Deux règles tenues : **l'identifiant du coursier vient du jeton, jamais du corps**, et
chaque route de progression vérifie que l'appelant est bien le coursier assigné.
`DbUpdateConcurrencyException` est traduit en **409**, le même code que la règle métier
`job.already_taken` — une seule branche à écrire côté application mobile.

### 2.4 Le balayage en tâche de fond — **fait**

`RedispatchBackgroundService`, toutes les 10 secondes (la fenêtre d'offre est de 25 s ;
balayer plus lentement laisserait une course inerte plus longtemps qu'elle n'a été
offerte). Interrupteur `Dispatch:Redispatch:Enabled` : **un seul hôte doit l'exécuter**,
la requête n'utilisant pas `SKIP LOCKED`.

### 2.5 Le transport marketplace → dispatch — **manquant, priorité 1**

Un `IIntegrationEventHandler<OrderReadyForDeliveryIntegrationEvent>` côté marketplace qui
POSTe vers `/internal/orders/ready-for-delivery`, signé. L'outbox porte déjà la persistance
et le réessai : il n'y a pas de bus à construire, seulement un appel à écrire.

Ajouter dans la foulée les deux autres émetteurs : commande annulée, et statut coursier
(depuis le module `Delivery`).

**Sans ce morceau, tout le reste du service est inerte** — et le reste silencieusement.

### 2.6 L'annonce de livraison — **manquant, priorité 2**

`DeliveryDelivered` → `POST /api/delivery/courses/completed`, déjà en place et signé côté
marketplace, idempotent sur l'identifiant de course.

Cela demande une **file persistée côté dispatch** (une petite outbox, avec sa migration) :
un POST en ligne perdrait le paiement si le réseau tombait entre la fermeture de la course
et l'annonce — course livrée, personne payé, rien qui le signale.

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

| # | Bloc | Pourquoi maintenant | État |
|---|---|---|---|
| 1 | Merger `wallet` | | fait dans `develop_staging` ; reste `develop` |
| 2 | Hôte du dispatch (§2.1 → 2.4) | Rien ne tourne sans | écrit, à compiler |
| 3 | Instruire le point BCEAO (§5.1) | Peut invalider le lot 2 — hors code, ça prend du temps | à lancer |
| 4 | Transport marketplace → dispatch (§2.5) | Sans lui le service tourne à vide, en silence | à faire |
| 5 | Annonce de livraison (§2.6) | Sans elle, une course livrée ne paie personne | à faire |
| 6 | `SKIP LOCKED` (§4.1) | Plafond dur, à lever avant la charge | à faire |
| 7 | Audit des routes (§4.2) | Même motif qu'une escalade déjà trouvée | à faire |
| 8 | Hub temps réel (§3) | Accélère ce qui marche déjà | à faire |
| 9 | Repli en cascade / 3PL | Le plus gros bloc restant du CDC | à faire |

Les points 2 et 3 se mènent en parallèle : l'un est du code, l'autre des démarches.

---

## 7. Ce qui n'est volontairement pas dans ce plan

- **L'application coursier (Flutter)** — client, hors du périmètre backend.
- **Le repli en cascade vers un 3PL** — bloqué par une question factuelle du CDC : existe-t-il
  un prestataire exposant une API exploitable dans la zone cible ? À vérifier avant de
  concevoir quoi que ce soit.
- **La preuve de livraison** (photo, code à 4 chiffres, signature) — décision produit ouverte
  au CDC §12.
