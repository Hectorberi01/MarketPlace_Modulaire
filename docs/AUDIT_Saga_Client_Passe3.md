# Audit de la saga client — passe 3

**Date** : 9 septembre 2026
**Périmètre** : le parcours d'achat complet, du panier au remboursement, plus le service coursier `HbaExpress.Dispatch`.
**Méthode** : lecture du code sur cinq segments menés en parallèle, puis recoupement. Aucune compilation, aucune exécution : ni le SDK .NET ni Postgres ne sont accessibles depuis l'environnement d'audit. Tous les `grep` excluent `bin/` et `obj/` — c'est l'erreur de la passe précédente, qui avait produit trois faux « c'est câblé ».
**Base** : arbre de travail au-dessus du commit `22fb8fd`, avec les corrections des §1.1 à §4.8 de `COHERENCE_Saga_Client.md` appliquées mais **non compilées ni testées**.

Chaque constat porte un fichier et une ligne. Les cinq défauts les plus graves ont été **revérifiés à la main** après le rapport des agents ; ils sont marqués `[revérifié]`.

---

## 1. Ce que l'audit change par rapport à la passe précédente

La passe 2 regardait la cohérence du parcours. Celle-ci a trouvé, dans les mêmes fichiers, une couche de défauts d'une autre nature : **des chemins d'erreur qui ne compensent rien**. Le parcours nominal est correct presque partout ; c'est ce qui se passe quand une étape échoue qui ne l'est pas.

Trois faits structurants, vérifiés, expliquent la majorité de ce qui suit :

1. **Il n'existe aucune transaction englobante.** Aucun `IPipelineBehavior` transactionnel, aucun `BeginTransaction` dans Ordering, Inventory ou Settlement. Chaque étape de la saga commite pour son propre compte : `ReserveStockCommandHandler:77`, `SettlementModuleApi:73`, puis la commande elle-même à la toute fin. Une saga sans transaction exige des compensations exhaustives ; elles ne le sont pas.

2. **Un `Result` d'échec ne remonte nulle part.** L'outbox ne rejoue que sur **exception** (`OutboxProcessor.cs:162`). Un handler qui renvoie `Result.Failure` est marqué « traité ». Tout le mécanisme de backoff, de lettre morte et de métrique `outbox_dead_letters_total` est contourné par le simple fait de renvoyer un échec typé au lieu de lever.

3. **Le résultat des appels inter-modules est ignoré au moins quatre fois**, dont deux sur des chemins d'argent. Deux agents indépendants ont trouvé le même défaut sur `ConfirmReservationAsync`.

---

## 2. Bloquants

### 2.1 La signature des webhooks FedaPay et Stripe est ouverte par défaut `[revérifié]`

`Payments.Infrastructure/Gateways/Real/FedaPayHttpGateway.cs:176`

```csharp
internal static bool VerifyFedaPaySignature(string rawBody, string? signatureHeader, string secret)
{
    if (string.IsNullOrEmpty(secret))
    {
        return true;
    }
```

Identique dans `StripeHttpGateway.cs:130`. Ces deux passerelles **surchargent** `ParseWebhookAsync` (`FedaPayHttpGateway.cs:113`) et ne passent donc jamais par `GatewayWebhook.Parse`, seul endroit qui consulte le garde-fou. Ce garde-fou existe pourtant, est fermé hors développement (`Program.cs:208`), et son commentaire décrit exactement le risque — il ne protège simplement pas les deux passerelles réellement déployées.

Rien n'empêche la configuration fautive : `PaymentGatewayOptions.cs:23` définit `IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)`. Le secret de webhook n'entre pas dans le test. Clé API renseignée + secret vide = passerelle réelle enregistrée et signature acceptée sans vérification.

La route est `AllowAnonymous()` **et** `DisableRateLimiting()` (`PaymentsEndpoints.cs:60`).

**Conséquence.** Avec un secret vide, un `POST` anonyme portant `{"name":"transaction.approved","entity":{"id":"<ref>","status":"approved"}}` déclenche la chaîne complète : stock décrémenté, expéditions créées, gains vendeur crédités, notifications envoyées. Une commande entièrement payée sans qu'un franc ait bougé. Le même défaut atteint les **versements** : `FedaPayPayoutGateway.cs:258` réutilise la même fonction, donc un faux `payout.sent` solde un retrait jamais versé.

**Ce qui protège aujourd'hui.** Un seul garde-fou, et il est dans un fichier de déploiement, pas dans le code : `deploy/docker-compose.prod.yml:65` utilise `${FEDAPAY_WEBHOOK_SECRET:?…}` — le `compose up` échoue si la variable manque. Le staging, lui, n'a pas ce garde : `docker-compose.staging.yml:66` est en `${FEDAPAY_WEBHOOK_SECRET:-}`.

**Aggravant.** Deux textes affirment le contraire du code. `deploy/docker-compose.prod.yml:56` dit « secret vide = tous les rappels REJETÉS » — c'est l'inverse. Et `tests/Payments.UnitTests/HttpGatewayTests.cs:126`, intitulé « LE test qui compte vraiment », n'exerce que `GatewayWebhook.VerifySignature`, jamais les deux fonctions des passerelles déployées.

---

### 2.2 Un gain vendeur peut être payé deux fois, par deux circuits qui s'ignorent `[revérifié]`

Le gain libéré à la livraison crédite le solde retirable :
`Earnings/ReleaseEarningsOnOrderDeliveredHandler.cs:55` → `ReleaseSellerAsync` → `Pending → Available`.

Le **même** gain, statut `Released`, alimente le lot de reversement :
`Batches/SettlementCommands.cs:51` `ListReleasedInPeriodAsync`, puis `:83` `batch.AddPayout`, puis `:92` `earning.MarkSettled`.

Et le versement de ce lot ne touche **aucun** portefeuille :

```csharp
// SettlementCommands.cs:165
var result = batch.MarkPayoutPaid(command.PayoutId, command.ProviderRef);
...
await _unitOfWork.SaveChangesAsync(cancellationToken);
```

`grep -n "Wallet" SettlementBatch.cs` ne renvoie rien. `PayoutPaidDomainEventHandler.cs:16` publie un événement dont le seul consommateur est Notifications.

Le circuit est vivant et exposé deux fois : `AdminFinanceEndpoints.cs:24` et `Marketplace.Api/Endpoints/SettlementEndpoints.cs:23`.

**Conséquence.** Un vendeur dont 500 000 XOF de gains sont libérés voit 500 000 sur son solde retirable **et** peut être payé 500 000 par un lot. Après `MarkPayoutPaid`, son solde retirable vaut toujours 500 000 : il demande un retrait, l'admin l'approuve, la plateforme paie une seconde fois. Aucun contrôle ne rapproche les deux circuits.

---

### 2.3 Une remise à montant fixe est multipliée par la quantité et par le nombre de lignes `[revérifié]`

Le prix envoyé à Pricing est **unitaire** (`CartPricer.cs:45` : `BaseAmount: item.UnitBaseAmount`), la remise fixe s'applique donc au prix unitaire :

```csharp
// Promotion.cs:257
PromotionType.FixedAmount => Math.Min(Value, amount),
```

puis elle est re-multipliée, ligne par ligne :

```csharp
// CartPricer.cs:58
var lineSeller   = b.SellerDiscount   * item.Quantity;
var linePlatform = b.PlatformDiscount * item.Quantity;
```

et une promo de périmètre « panier » ou « global » est déclarée applicable à **chaque** ligne (`Promotion.cs:242`).

**Conséquence.** Un coupon « −500 XOF » sur un panier de 2 lignes à 3 unités retire **3 000 XOF** au lieu de 500. Le décompte, lui, ne compte qu'une utilisation (`RedeemPromotionOnOrderConfirmedHandler.cs:131`) : le plafond du coupon ne protège de rien.

---

### 2.4 `POST /api/cart/checkout` détruit le panier sans créer de commande `[revérifié]`

Route exposée (`CartEndpoints.cs:26`), qui appelle `CheckoutCartCommand` :

```csharp
// CartCommands.cs:133
var result = cart.MarkCheckedOut();
...
await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
return cart.Id.Value;
```

Le panier passe `CheckedOut`. La seule lecture du panier actif filtre sur `Active` (`CartRepository.cs:21`), `Cart.cs` n'a aucune méthode de réouverture, et `CartCheckedOutIntegrationEvent` **n'a aucun consommateur** — vérifié sur `src` et `tests`, hors `bin`/`obj` : les deux seules occurrences sont le contrat et le publieur.

**Conséquence.** L'acheteur perd son panier, définitivement, sans qu'aucune commande n'existe. Aucun recours en libre-service.

Deux documentations mensongères sur ce même chemin : `CartCommands.cs:19` (« déclenche la création de commande côté Ordering ») et `CartCheckedOutDomainEventHandler.cs:8` (« consommé par Ordering / analytics »).

---

### 2.5 La saga ne compense rien sur le chemin d'exception

`PlaceOrderCommandHandler.cs:171-191` : la libération du stock déjà réservé n'est déclenchée que par `ok == false`. Or `TryReserveAsync` **lève** sur le cas le plus attendu en production — le conflit de verrou optimiste, documenté comme tel dans `InventoryItem.cs:60` et traduit en 409 par `ConcurrencyExceptionHandler.cs:126`. Aucun `try/catch` entre les deux.

**Conséquence.** Deux acheteurs sur le dernier article, ou toute coupure au milieu de la boucle : les lignes déjà réservées ne sont **jamais** libérées, et la commande n'est jamais persistée (seul `SaveChangesAsync`, ligne 238). Des réservations orphelines rattachées à un `orderId` qui n'existe dans aucune table, que le balayeur ne peut pas voir puisqu'il part de `_dbContext.Orders` (`OrderRepository.cs:52`). Seule l'expiration à 6 h les rend inoffensives. Le message du 409 — « Rien n'a été enregistré. Réessayez. » — est faux.

---

### 2.6 La cagnotte engagée n'a aucune compensation dans l'orchestrateur

`PlaceOrderCommandHandler.cs:206-213` appelle `TryReserveCreditAsync`, qui commite pour son propre compte (`SettlementModuleApi.cs:73`). Après cet appel, `ReleaseCreditAsync` **n'apparaît nulle part dans ce fichier**. Son seul appelant, sur tout le dépôt, est `CancelOrderCommandHandler` (`OrderLifecycleCommands.cs:152`) — qui exige une commande chargée depuis la base.

**Conséquence.** Tout échec entre l'engagement et le `SaveChanges` final laisse la cagnotte dans le compte `Reserved` **définitivement**. Aucune commande à annuler, aucun balayeur qui regarde le grand livre, et `GetSpendableCreditAsync` exclut le réservé. L'argent du client disparaît de son solde utilisable, sans explication. Le stock a un second filet — `ExpiresAtUtc` ; la cagnotte n'en a aucun.

---

### 2.7 Commande payée en cagnotte : l'argent part avant que la commande n'existe

`PlaceOrderCommandHandler.cs:253-271` (`SettleWithWalletOnlyAsync`, appelé ligne 231) débite la cagnotte et décrémente le stock — chacun avec son propre commit — puis la commande n'est écrite qu'à la ligne 238. Or ce `SaveChangesAsync` peut échouer : `ModuleDbContext.SaveChangesAsync:53` dispatche les événements de domaine **avant** `base.SaveChangesAsync`, donc un handler qui lève emporte la sauvegarde.

**Conséquence.** Le client a payé, le stock est parti, et **aucune ligne de commande n'existe**. Rien n'est rattrapable : ni le balayeur, ni les plafonds de remboursement, qui ont tous besoin d'un `orderId`. C'est le pire état atteignable du parcours.

---

### 2.8 Le résultat de `ConfirmReservationAsync` est ignoré aux deux sites d'appel — survente silencieuse

Trouvé indépendamment par deux agents.

- `OrderLifecycleCommands.cs:94` — chemin webhook
- `PlaceOrderCommandHandler.cs:260` — chemin cagnotte intégrale

La signature est `Task<bool>` (`IInventoryModuleApi.cs:20`) et le `false` est significatif : `InventoryItem.ConfirmReservation:193` renvoie `NotFound` quand rien n'est réservé.

**Conséquence.** `order.Confirm()` s'exécute, l'expédition est créée, le vendeur est crédité — **sans que `OnHand` ait été décrémenté**. La marchandise est vendue deux fois.

Ce n'est pas une hypothèse : le défaut est documenté **deux fois dans le dépôt comme un fait acquis** (`InventoryItem.cs:186` et `tests/Inventory.UnitTests/ExpirationDesReservationsTests.cs:74`). Le code s'est organisé autour du bug au lieu de le corriger — y compris la correction du §3.2 de la passe précédente, qui a délibérément préservé le comportement pour ne pas l'aggraver.

---

### 2.9 Paiement encaissé sur commande annulée : l'échec est avalé, le message est marqué traité

`Ordering/.../EventHandlers/PaymentOutcomeHandlers.cs:19`

```csharp
public Task HandleAsync(PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    => _sender.Send(new ConfirmOrderPaymentCommand(integrationEvent.OrderId), cancellationToken);
```

`Task<Result>` rendu comme `Task` : le `Result` est jeté. Aucun behavior ne le convertit en exception. Idem ligne 30 pour l'annulation.

**Scénario atteignable, deux variantes.** L'acheteur annule pendant que le paiement est en vol (`Order.Cancel` l'autorise depuis `AwaitingPayment`, route publique `OrderingEndpoints.cs:112`) ; ou le balayeur annule la commande à 6 h et le paiement Mobile Money aboutit quelques minutes plus tard. Dans les deux cas `MarkPaid` refuse, l'échec disparaît, `OutboxProcessor.cs:162` pose `ProcessedOnUtc`.

**Conséquence.** Acheteur débité, commande annulée, **aucun remboursement, aucun rejeu, aucune lettre morte, aucune alerte** — un `LogWarning` noyé dans le flux.

---

### 2.10 Remboursement direct : le versement part avant le moindre `SaveChanges`

`CustomerRefundCommands.cs` : plafond lu ligne 119-129, payout ligne 187, persistance ligne 255. Entre les deux, rien n'est réservé — aucun index unique sur `customer_refunds` (`WalletConfigurations.cs:157` : trois index, aucun unique), aucune ligne réclamée.

**Conséquence A, concurrence.** Deux administrateurs sur une commande à 13 000 lisent tous deux `remaining = 13 000` et déclenchent tous deux le payout : 16 000 quittent la caisse.

**Conséquence B, pire.** Le portefeuille plateforme porte un jeton `xmin` que l'accrual salit. Le second `SaveChanges` lève donc `DbUpdateConcurrencyException` → 409 → **tout est annulé : ni ligne de remboursement, ni écriture de grand livre, ni débit plateforme — alors que l'argent est parti chez FedaPay.** Le plafond ne le verra jamais, et l'admin qui relance sur un 409 en envoie un troisième.

C'est exactement la panne corrigée sur les retraits, dont l'explication est à quinze lignes de là dans le même module (`WalletCommands.cs:145` : « ON RÉCLAME LA LIGNE AVANT D'APPELER LE PRESTATAIRE. L'ORDRE EST TOUT »). La correction n'a pas été portée sur ce rail.

---

### 2.11 Une course échouée est un cul-de-sac des deux côtés

`POST /api/courier/jobs/{id}/failed` existe (`Dispatch/.../CourierEndpoints.cs:60`) et pose `Status = Failed` (`DeliveryJob.cs:364`). Trois faits vérifiés :

- `JobProgressService.FailAsync:148` n'écrit **aucun** message sortant ; `OutboundMessage` ne connaît qu'un seul type, `courier_delivery_completed`.
- Le balayage ne reprend jamais une course échouée (`DispatchRepositories.cs:122` : `WHERE Status IN ('Created','Offered')`).
- Rien côté monolithe ne consomme un échec de course.

**Conséquence.** Acheteur absent → le coursier déclare l'échec, il est libéré, et l'expédition reste `Preparing` **pour toujours**. Pas de `ShipmentDelivered`, donc la commande reste `Confirmed`, donc ni l'escrow Payments ni les gains Settlement ne sont libérés. Aucune erreur, aucune métrique. C'est la panne que le §1.1 de la passe précédente prétendait avoir fermée, déplacée d'un état.

---

### 2.12 Un incident de ramassage fige la course, et le domaine affirme le contraire

`JobLocations.cs:89` : `ReportIncident` note la raison mais laisse `IsPickedUp == false`. Et `DeliveryJob.cs:334` refuse le départ tant qu'un ramassage manque.

Le commentaire de `ReportPickupIncident` (`DeliveryJob.cs:309`) dit : « on note et on continue les autres ». **On ne continue rien.** Un seul vendeur défaillant fige la course, dont la seule sortie est `Fail` — c'est-à-dire §2.11.

**Conséquence.** Les parts des autres vendeurs, déjà ramassées, ne sont jamais livrées ; leur séquestre n'est jamais libérée.

---

## 3. Sérieux

**3.1 Deux consommateurs de `OrderConfirmed` sur huit n'ont aucune garde d'idempotence.** Les huit ont été vérifiés un par un : `OrderNotificationHandlers.cs:27` et `SellerOrderNotificationHandler.cs:52` créent inconditionnellement (`NotificationDispatcher.cs:53`), sans filet en base (`NotificationConfiguration.cs:30`, index non unique). Aggravant : ils passent **avant** Settlement, Loyalty et Analytics dans l'ordre de dispatch (`ModuleRegistry.cs:38`), donc une panne dans l'un de ceux-ci rejoue le message entier. Jusqu'à dix exemplaires avant la lettre morte (`OutboxRetryPolicy.cs:26`). L'acheteur reçoit dix fois « Votre paiement a été reçu ».

**3.2 La devise envoyée au PSP est celle de la configuration, pas celle de la commande.** `FedaPayHttpGateway.cs:59` : `currency = new { iso = _options.Currency }` — idem MTN et Moov. Stripe fait correctement le travail (`StripeHttpGateway.cs:50`). Une commande en EUR devient une charge en XOF de même valeur numérique. Cumulatif avec `(long)Math.Round(context.Amount)` ligne 58, qui tronque les décimales.

**3.3 Arrondi à deux décimales dans une devise qui n'en a pas.** `Money.cs:35` arrondit à 2 décimales, `Promotion.cs:257` produit des centimes, et les passerelles tronquent à l'entier. La commande enregistre 8 999,10 XOF, le prestataire encaisse 8 999, et la commande est marquée payée. Ces décimales n'existent nulle part dans le monde réel mais alimentent `TaxTotal`, `NetTotal` et la base de commission.

**3.4 Le panier n'est fermé qu'en asynchrone.** `PlaceOrderCommandHandler` ne touche jamais le panier ; la clôture réagit à un événement d'outbox dépilé toutes les 5 s (`OutboxProcessor.cs:74`). Pendant cette fenêtre, un second `/checkout/pay` sans `Idempotency-Key` — l'en-tête est explicitement facultatif — crée une **seconde commande complète**. Et le commentaire qui justifie l'idempotence affirme le contraire du code : `ClientCheckoutEndpoints.cs:147` prétend que « `PlaceOrderCommand` VIDE LE PANIER ». Il ne vide rien.

**3.5 Le plafond « par commande » des remboursements se contourne par concurrence.** `ReturnLifecycleCommands.cs:153` lit, décide, écrit — sans verrou, et `ReturnRequestConfiguration` ne pose aucun jeton de concurrence. Deux approbations simultanées sur deux lignes d'une même commande passent toutes les deux.

**3.6 N'importe quel compte authentifié peut dépublier n'importe quel avis** `[revérifié]`. `ReviewsEndpoints.cs:27` : `/flag` exige `RequireAuthorization()` sans rôle, et `FlagReviewCommandHandler:52` ne contrôle rien. `Review.Flag()` pose `Flagged`, que les trois lectures publiques excluent (`ReviewRepository.cs:20,40,58`). Un compte créé ce matin fait tomber la note d'une boutique à zéro, avis par avis. Le BFF vendeur, lui, contrôle bien la propriété : la garde existe, elle a été oubliée sur l'hôte monolithe.

**3.7 Le litige ne gèle rien, et trois textes affirment le contraire.** `Dispute.cs:8`, `DisputeDomainEventHandlers.cs:8` et `DisputesIntegrationEvents.cs:5` annoncent un gel d'escrow par Settlement et Payments. `grep -rn "Dispute" src/Modules/Settlement src/Modules/Payments src/Modules/Ordering` : **aucune occurrence**. Un litige ouvert n'empêche ni la libération de la séquestre ni le retrait du vendeur. La décision « le litige arbitre, il ne paie pas » est assumée et documentée côté admin ; ce sont le domaine et les contrats qui mentent.

**3.8 Un vendeur ne peut pas ouvrir de litige.** `OpenDisputeCommand.cs:60` compare `order.Lines.Any(l => l.SellerId == command.RaisedBy)` — mais `RaisedBy` est un identifiant de **compte** et `SellerId` un identifiant de **boutique** (`SellerSummary` porte `Id` et `UserId` comme deux champs distincts). La branche est **toujours fausse**. Et `SellerDisputeEndpoints` n'expose aucune route d'ouverture. Un vendeur lésé reçoit `404 Commande introuvable`.

**3.9 Une expédition annulée empêche définitivement la commande d'atteindre « livrée ».** `MarkOrderDeliveredOnAllShipmentsDeliveredHandler.cs:37` exige `All(s => s.Status == "Delivered")` sans exclure `Cancelled`, alors que le chemin coursier saute délibérément les annulées (`ShipmentCommands.cs:170`). Commande à deux vendeurs, l'un annule, l'autre livre → la commande reste `Confirmed` indéfiniment, `DeliveredAtUtc` reste nul, donc la fenêtre de retour ne démarre jamais et l'escrow n'est jamais levée.

**3.10 Une part déjà confiée à un transporteur est quand même envoyée au coursier comme point de ramassage.** `PublishOrderReadyForDeliveryHandler.cs:95` n'écarte que les annulées ; une expédition `Shipped` reste dans les `Pickups`. Le coursier est envoyé chez une boutique qui n'a rien à lui remettre — et ne peut que déclarer un incident, donc §2.12. Les deux moitiés de la même règle divergent.

**3.11 Le plafond de rémunération de course n'est pas cumulatif.** `CourierEarningCommands.cs:115` compare à `order.ShippingFee` **par course**, la clé d'idempotence étant `DeliveryJobId`. N courses sur une commande à 1 500 de frais → jusqu'à N × 1 500 sortent. `CourierPayLimits.cs:20` énonce pourtant l'inverse comme « un invariant, pas un réglage ». Le compte `Shipping` autorise le négatif, donc rien ne bloque ni n'alerte.

**3.12 Réconciliation des remboursements client : `continue` muet et lecture non bornée.** `CustomerRefundCommands.cs:297` saute en silence une ligne sans référence PSP — exactement le défaut corrigé sur les retraits quinze lignes plus loin (`ReconcileWithdrawalsCommand.cs:80`, `LogCritical`). Et le `Take(BatchSize)` est **en mémoire** (`WalletRepositories.cs:120` puis `CustomerRefundCommands.cs:293`), quand le voisin le fait correctement en SQL. Comme le premier défaut empêche les lignes de sortir de `Processing`, la table de travail ne fait que grossir.

**3.13 Retraits client et coursier « Processing » invisibles de la console.** `WalletQueries.cs:204` ne liste que `WalletOwnerType.Seller`. L'état où le bénéficiaire est débité sans avoir été payé — celui que la requête elle-même documente comme le seul qui compte — n'est consultable que pour les vendeurs.

**3.14 Fenêtre de survente entre l'expiration de la réservation et le passage du balayeur.** Réservation à 6 h (`ReservationCommands.cs:38`), abandon à 6 h (`AbandonedOrderSweeper.cs:31`), balayage toutes les 15 min par lots de 100. À T+6 h le stock redevient vendable **immédiatement**, mais la commande reste `AwaitingPayment` — donc confirmable — jusqu'à 15 minutes de plus. Et `ConfirmReservation` n'a aucun plancher : `OnHand` peut passer sous zéro. Le commentaire affirme que « la fenêtre de réservation ne peut pas être plus courte que la fenêtre de paiement » ; les deux sont **égales**, et l'écart du balayeur n'est nulle part pris en compte.

**3.15 Prix figés à l'ajout au panier, jamais rafraîchis.** `AddItemToCartCommandHandler.cs:81` est le seul appel à `GetOfferAsync` de tout le chemin. Un panier laissé trois semaines est payé au prix d'il y a trois semaines. Le gel du barème de commission est délibéré et documenté ; le gel du prix de base sans expiration ne l'est nulle part.

**3.16 Le stock est vérifié sur la quantité ajoutée, jamais sur le cumul.** `AddItemToCartCommandHandler.cs:63` teste `command.Quantity`, puis `Cart.cs:90` incrémente. `UpdateCartItemQuantityCommandHandler` ne consulte jamais Inventory, et le validateur ne pose aucun plafond. Cinquante « +1 » sur une offre à 1 exemplaire construisent un panier de 50, valorisé et affiché normalement ; l'échec ne survient qu'au placement, après l'écran d'adresse.

**3.17 La condition « montant minimum » est évaluée par ligne.** `CartPricer.cs:52` passe `Subtotal: item.UnitBaseAmount * item.Quantity`. Une promo « à partir de 50 000 » est refusée sur un panier de 100 000 fait de 10 lignes à 10 000 — et accordée sur **toutes** les lignes dès qu'une seule franchit le seuil.

**3.18 Le handler de clôture par coursier neutralise la garde d'idempotence de l'agrégat.** `Shipment.MarkDeliveredByCourier` compare l'identifiant de course (`Shipment.cs:210`), avec le commentaire qui explique pourquoi. `ShipmentCommands.cs:175` fait `continue` sur `Delivered` sans jamais comparer `DeliveryJobId`, et `DeliveryEndpoints.cs:184` enchaîne sur le crédit coursier avec le **nouveau** `DeliveryJobId`, dont le verrou de rejeu ne connaît rien. Non atteignable par le chemin nominal — une seule course par commande — mais la garde écrite pour ce cas est contournée.

---

## 4. Mineurs et dette

- **Devise du tarif de livraison jamais confrontée à celle du panier.** `ShippingRate.cs:60` accepte n'importe quel code à trois lettres ; `ClientCheckoutEndpoints.cs:480` additionne `rate.Amount` à `cart.GrandTotal` sans contrôle. Le panier, lui, refuse les devises mêlées.
- **Aucune fenêtre anti-rejeu horodatée sur les webhooks PSP.** Le `t` est signé mais jamais comparé à l'heure courante (`FedaPayHttpGateway.cs:209`). Nuisance bornée, pas vol.
- **Aucune réconciliation périodique des paiements bloqués.** La seule reprise est déclenchée quand l'acheteur retente (`InitiatePaymentCommandHandler.cs:170`). Un `Pending` dont le webhook n'arrive jamais reste `Pending` éternellement.
- **`Clear()` ne retire pas le code promo et ne vérifie pas le statut** (`Cart.cs:142`). L'ancien coupon s'applique silencieusement au panier reconstitué.
- **`PartialRefund` impossible via `/api/disputes/{id}/resolve`** : `DisputesEndpoints.cs:159` n'a pas de champ montant, que le domaine exige. Le BFF Admin, lui, le transmet.
- **Cache de 2 minutes du panier valorisé** (`CartQueries.cs:19`) : devis et paiement lisent la même entrée, donc `expectedTotal` ne détecte rien — les deux sont d'accord sur une valeur potentiellement périmée.
- **Code mort vérifié** : `IWalletRepository.GetByIdAsync`, `ICustomerRefundRepository.GetByIdAsync`, `SumForAccountAsync`, `Withdrawal.WalletId`, `IShippingZoneRepository.Remove`, `ICartModuleApi.GetCartAsync` (seul appelant : un faux de test), `StockReservedIntegrationEvent` (aucun consommateur), `OrderStatus.Paid` (jamais persisté), le chemin « commande annulée → course annulée » (inatteignable).
- **Documentation mensongère, au-delà de celles déjà citées** : `DeliveryEndpoints.cs:35` réclame un plafond coursier qui existe déjà ; `docs/06-modules-reference.md:62` décrit `SellerWallet` et `PlatformWallet`, supprimés par la migration `UnifiedWallets` ; `InventoryDomainEventHandlers.cs:8` annonce un couplage avec Ordering qui n'existe pas.
- **`docs/COHERENCE_Saga_Client.md` est périmé** : §1.1, §1.2, §4.5, §4.7, §4.1 et §4.2 sont marqués « [vérifié] » et décrivent des défauts corrigés depuis. §4.3 reste exact.
- **`AddInvoiceLineCommand` sans validateur** : `Invoice.AddLine` accepte n'importe quel montant, négatif compris. Sans effet sur les portefeuilles.
- **Métrique Prometheus incrémentée avant la garde d'idempotence** (`AccrueMetricsOnOrderConfirmedHandler.cs:51`) : le GMV Prometheus peut dériver au-dessus du GMV en base.

---

## 5. Ce qui est solide

Cette partie n'est pas de la politesse : ce sont les pièces à ne pas casser en corrigeant le reste.

**Les corrections de la passe précédente tiennent toutes.** Vérifiées une par une :

- `expectedTotal` est posé exactement au bon endroit — après le recalcul des totaux, avant toute écriture — avec égalité stricte, `Conflict` et non `Validation`, et un test qui verrouille le fait qu'un refus **n'écrit et ne réserve rien**.
- Le régime TVA comprise est juste : extraction `TTC × taux / (1 + taux)`, hors taxe obtenu par soustraction, plafonnement **et** re-plafonnement si le total baisse ensuite. Le piège `TTC × taux` est nommé et évité.
- Le prix de livraison n'a plus qu'une source : devis et paiement appellent le même module, le second pour la commune **effectivement figée sur la commande**. L'index unique partiel « une seule zone de repli active » existe réellement en migration.
- `RefundSplit` est pure, borne le disponible espèces à zéro, calcule la part cagnotte comme un reste. Les deux jambes consomment bien le plafond de valeur ; seule la caisse borne la caisse. La jambe cagnotte échappe à la réconciliation PSP par **double** barrière.
- Le retrait réclame bien sa ligne avant d'appeler le prestataire, le jeton `xmin` existe, et les lignes orphelines sont remontées en `LogCritical`.
- `LedgerIntegrityService` est réellement enregistré et atteignable, alerte sans corriger, et détecte les écritures orphelines — le seul cas qu'un parcours des portefeuilles ne verrait jamais.
- La fenêtre de retour de sept jours, les plafonds ligne et commande, et le traitement des valeurs aberrantes sont corrects.

**Le reste du socle :**

- **L'outbox.** Vrai `SELECT … FOR UPDATE SKIP LOCKED` dans une transaction explicite, backoff exponentiel avec gigue, lettre morte en `Critical` avec métrique. Le blocage de tête de file est réellement fermé — c'est le seul mécanisme qui manque à l'appel quand un handler renvoie un `Result` au lieu de lever (§1, point 2).
- **L'invariant « solde = somme des écritures » tient sur tous les chemins d'écriture** : `Credit`/`Debit`/`Transfer` n'apparaissent que dans `WalletMutations` et `Wallet`. Un transfert écrit bien deux lignes. La migration de reprise écrit une écriture d'ouverture par compte.
- **Le verrou optimiste est effectif, et le piège de l'insertion d'enfant est traité** : `Touch()` force un `UPDATE` du parent, sans quoi `xmin` ne serait jamais confronté. Même dispositif sur `Wallet`.
- **Trois références d'idempotence distinctes pour la cagnotte**, avec gardes croisées : confirmer après avoir libéré, ou libérer après avoir dépensé, sont tous deux refusés et journalisés.
- **L'ordre stock-puis-cagnotte** est le bon : l'échec le plus fréquent n'engage jamais d'argent.
- **HMAC du service coursier, les deux sens** : horodatage dans la chaîne signée, comparaison en temps constant, fenêtre ±5 min, secrets distincts par direction, **fermé par défaut** en production, corps borné à 64 Ko avant vérification, signature sur les octets bruts. C'est le contre-exemple exact de §2.1.
- **Anti-IDOR sur tout le parcours client** : commandes, paiements, retours, litiges, expéditions, cagnotte, retraits — identité toujours issue du jeton, `404` et non `403`, corps identique sur toutes les branches de refus. Clé d'idempotence hachée **avec** l'identifiant acheteur.
- **Rédemption des coupons** : décompte à la confirmation, index unique `(PromotionId, OrderId)`, jeton `xmin` qui sérialise réellement les rédemptions concurrentes. C'est le seul des huit consommateurs qui s'appuie sur une contrainte de base et non sur un `SELECT` préalable.
- **Avis** : unicité garantie en base par index unique, cache invalidé dans le `SaveChanges` du contexte.
- **`GrossSplit`** : le net est un reste, l'invariant `brut = commission + provider + net` tient au franc près, et les taux sont lus sur la ligne de commande.
- **Le montant demandé au PSP est `AmountDue`, pas `GrandTotal`**, avec refus explicite si la cagnotte couvre tout.
- **Double paiement coursier fermé** : verrou d'idempotence sur `DeliveryJobId`, message sortant et fermeture de course dans une seule transaction, arrondi vers le bas.
- **Un seul gagnant sur une course** : garde métier **et** jeton de concurrence, avec un test d'intégration dédié.
- **`CartConfiguration.cs:34`** mérite d'être cité en exemple : un commentaire antérieur y est conservé mais **explicitement marqué comme non vérifié**, avec l'explication de pourquoi il est probablement faux. C'est le contraire des documentations mensongères relevées ailleurs.

---

## 6. Ordre de réparation proposé

Le critère est simple : d'abord ce qui laisse sortir de l'argent ou entrer un attaquant, ensuite ce qui casse un parcours, enfin ce qui ment.

1. **§2.1 — la signature des webhooks.** Faire passer FedaPay et Stripe par le garde-fou existant, et faire entrer le secret de webhook dans `IsConfigured`. Une heure de travail pour la vulnérabilité la plus grave du dépôt.
2. **§2.2 — le double rail de paiement vendeur.** Trancher : soit le lot de reversement débite le portefeuille, soit il disparaît. Aucune version intermédiaire n'est tenable.
3. **§2.10 — réclamer la ligne de remboursement avant le payout.** La correction existe déjà quinze lignes plus loin ; il s'agit de la recopier.
4. **§2.3 — la remise à montant fixe.** Le calcul doit se faire sur le panier, pas par ligne. Touche `CartPricer` et le contrat `PriceRequest` ; §3.17 se corrige avec.
5. **§2.8 — lire le résultat de `ConfirmReservationAsync`.** Deux sites, mécanique. C'est la survente.
6. **§2.9 — ne plus avaler les `Result` d'échec dans les handlers d'intégration.** Un défaut de mécanisme, pas de logique : traité une fois, il ferme aussi des trous que cet audit n'a pas trouvés.
7. **§2.5, §2.6, §2.7 — les compensations de la saga.** Le plus gros morceau : un `try/catch` autour de la boucle de réservation, une compensation de cagnotte dans l'orchestrateur, et l'écriture de la commande **avant** toute consommation définitive.
8. **§2.11, §2.12 — sortir une course de l'échec.** Un message sortant sur `Fail`, et un ramassage en incident qui ne bloque plus les autres.
9. **§2.4 — supprimer `POST /api/cart/checkout`**, comme `POST /api/orders` l'a été : c'est la même route morte, en pire, puisqu'elle détruit des données.
10. **§3.6 — le rôle manquant sur `/flag`.** Une ligne.
11. **§3.1 — l'idempotence des deux handlers de notification.**
12. **§3.2, §3.3 — la devise et l'arrondi.** Décision produit : le XOF n'ayant pas de centime, faut-il arrondir à l'unité dès `Money` ?
13. Le reste par ordre décroissant, en commençant par les documentations mensongères — elles coûtent peu à corriger et font perdre le plus de temps.

---

*Audit mené sans compilateur ni base de données. Les constats sont établis par lecture ; aucun n'a été reproduit à l'exécution. Les cinq plus graves ont été revérifiés à la main après le rapport initial.*
