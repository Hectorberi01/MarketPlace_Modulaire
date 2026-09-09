# Correction globale — audit passe 3

Base : commit `9dc7f46` « vérification saga client v2 ». 99 fichiers modifiés ou ajoutés.
**Rien n'a été compilé ni testé** — aucun SDK .NET ni base accessible depuis l'environnement de travail.

---

## Ce qu'il faut faire avant tout

Quatre migrations, dont **deux bloquantes** : sans elles le module concerné plante à la première écriture.

```
dotnet ef migrations add AddOrderLineTotal            -p src/Modules/Ordering/Marketplace.Modules.Ordering.Infrastructure
dotnet ef migrations add AddNotificationDeduplicationKey -p src/Modules/Notifications/Marketplace.Modules.Notifications.Infrastructure
dotnet ef migrations add AddShipmentCourierFailure    -p src/Modules/Shipping/Marketplace.Modules.Shipping.Infrastructure
dotnet ef migrations add AddCustomerRefundChannel     -p src/Modules/Settlement/Marketplace.Modules.Settlement.Infrastructure
```

**`AddOrderLineTotal` demande une reprise de données à écrire à la main** dans le `Up()` généré, après l'`AddColumn` et avant le `DropColumn` :

```sql
UPDATE ordering.order_lines
   SET "LineTotal"        = "FinalUnitPrice" * "Quantity",
       "SellerDiscount"   = "SellerDiscount" * "Quantity",
       "PlatformDiscount" = "PlatformDiscount" * "Quantity";
```

Sans elle, **toutes les commandes existantes deviennent fausses** : les remises étaient unitaires, elles sont désormais des montants de ligne. Le `Down()` doit faire l'inverse (division par `Quantity`, en se gardant du zéro).

Puis :

```
dotnet build Marketplace.sln && dotnet test Marketplace.sln
```

---

## 1. §2.1 — la signature des webhooks

`VerifyFedaPaySignature` et `VerifyStripeSignature` retournaient `true` sur secret vide. Comme ces deux passerelles surchargent `ParseWebhookAsync`, elles ne traversaient jamais `GatewayWebhook.Parse`, seul endroit qui consultait le garde-fou `AllowUnsignedWhenSecretMissing` — lequel était pourtant écrit, commenté et fermé en production. Les deux consultent désormais ce garde-fou.

Ajouté au passage, parce que c'était cinq lignes et le même fichier : **la fenêtre anti-rejeu**. L'horodatage entrait dans le HMAC mais n'était jamais confronté à l'heure courante. `GatewayWebhook.IsWithinClockSkew` — cinq minutes, symétrique, horodatage illisible refusé — est appelée après la vérification de signature, sur la valeur parsée.

**Garde-fou de démarrage** dans `PaymentsModuleInstaller` : en production, une passerelle réelle sans secret de webhook fait échouer le boot, avec un message qui dit ce qui manque et pourquoi. Volontairement séparé de `IsConfigured` — « peut encaisser » et « peut vérifier » sont deux garanties distinctes, et élargir `IsConfigured` aurait fait retomber silencieusement sur une passerelle simulée hors production.

Le commentaire de `docker-compose.prod.yml` affirmait que les rappels seraient « rejetés » : c'était l'inverse, il est corrigé. `docker-compose.staging.yml` dit maintenant ce qu'implique un secret vide.

**Le test qui prétendait verrouiller ce défaut ne l'a jamais touché.** `tests/Payments.UnitTests/SignatureDesPasserellesTests.cs` appelle les deux fonctions par leur nom : secret manquant, corps modifié, rejeu ancien, horodatage futur, fenêtre symétrique. La doc de l'ancien test dit désormais ce qu'il ne couvre pas.

---

## 2. §2.2 — le double rail de paiement vendeur

`MarkPayoutPaid` ne touchait aucun portefeuille : après un lot payé, le solde retirable du vendeur était intact, il demandait un retrait, et le même gain sortait une seconde fois.

Le handler débite désormais `Available` du montant net, référencé par l'identifiant du reversement (`WalletMutations.DebitSellerBatchPayoutAsync`, référence `batch_payout`). Les deux rails puisent dans le même solde, et le second passage est fermé par `Withdrawal.Create` — qui refuse déjà un retrait supérieur au disponible — sans qu'aucune règle nouvelle n'ait à l'énoncer.

Idempotence à deux étages : le statut du reversement lu **avant** la transition, puis le verrou de grand livre en second rideau.

**Le négatif est autorisé, et c'est le point.** Ce reversement enregistre un virement que l'administrateur a déjà fait à la main : le refuser ne rappellerait pas l'argent, cela effacerait la trace de sa sortie. Un solde négatif après ce débit signifie que le vendeur avait déjà retiré — un `LogCritical` le dit, sans correction automatique.

---

## 3. §2.10 — réclamer la ligne de remboursement avant le versement

Le payout partait avant le moindre `SaveChanges` : le plafond ne voyait rien de ce qui était en vol, et un conflit sur le jeton du portefeuille plateforme annulait tout — ni ligne, ni écriture, ni débit — alors que l'argent était parti. La correction existait déjà quinze lignes plus loin, sur les retraits ; elle a été portée.

Le coût assumé — une ligne « en cours » sans référence prestataire si le processus meurt — est instrumenté dans la foulée : le `continue` muet de la réconciliation devient un `LogCritical` au-delà d'une heure, comme pour les retraits. Et le lot est désormais borné **en SQL** (`ListProcessingAsync(take, …)`) et non plus tronqué en mémoire après avoir tout matérialisé.

---

## 4. §2.3 et §3.17 — la remise à montant fixe

Trois multiplications se cumulaient : le prix envoyé à Pricing était unitaire, l'appelant multipliait le résultat par la quantité, et une promo de portée « panier » était déclarée applicable à chaque ligne. Un coupon de 500 sur deux lignes de trois articles en retirait 3 000, pour une seule utilisation décomptée.

Le contrat distingue maintenant explicitement les deux niveaux :

- `PriceRequest` : `BaseAmount` → **`LineAmount`** (montant de ligne), `Subtotal` → **`CartSubtotal`** (panier, pour les conditions), plus `TotalQuantity`.
- `PriceCalculator.Calculate` **exclut** les remises à montant fixe ; `PriceCalculator.CalculateCartLevel` ne traite qu'elles, une fois, sur le sous-total. Le critère est le **type**, pas la portée : un pourcentage est linéaire, donc juste ligne par ligne.
- Nouveau `IPricingModuleApi.CalculateCartDiscountAsync`.
- `CartPricer` fait deux passes, puis répartit la remise de panier au prorata avec **la dernière ligne en reste** — trois lignes égales et 500 de remise donneraient sinon 500,01, un centime créé de rien à chaque commande.

Une remise fixe **ciblée sur un produit** est écartée des deux calculs : « −500 sur les chaussures » n'a pas de sens univoque, et l'interpréter au hasard coûterait de l'argent.

### La conséquence en aval : `OrderLine` devient une ligne

`CartLineSummary` portant désormais des montants de ligne, garder `OrderLine` en unitaire aurait produit une dérive d'arrondi entre le total affiché et le total facturé — que `expectedTotal`, en égalité stricte, aurait transformée en refus de paiement.

`OrderLine.LineTotal` est donc **stocké** et `FinalUnitPrice` **calculé** (affichage seul). `Order.RecomputeTotals` ne multiplie plus les remises par la quantité, et `AccrueEarningsOnOrderConfirmedHandler` calcule le brut vendeur comme `prix unitaire × quantité − remise`. C'est cette bascule qui exige la reprise de données ci-dessus.

Les trois tests de `PriceCalculatorTests` qui décrivaient l'ancien comportement sont réécrits, pas supprimés : même scénario, découpé en deux appels, même total final.

---

## 5. §2.8 — la survente silencieuse

`ConfirmReservationAsync` renvoie un booléen significatif, ignoré aux deux sites d'appel. La commande passait `Confirmed`, l'expédition était créée, le vendeur crédité — sans que `OnHand` bouge.

Le résultat est lu, et la confirmation refusée sinon (`ordering.reservation_not_settled`). Deux commentaires du dépôt décrivaient ce défaut comme un fait acquis, et un test le documentait : les trois sont corrigés pour dire ce qui est vrai maintenant.

**Compromis assumé** : un article devenu suivi en stock entre le placement et la confirmation fera désormais échouer la confirmation d'une commande payée, là où elle passait en survente. Rare, et bruyant plutôt que muet.

---

## 6. §2.9 — les échecs qui disparaissaient

`ConfirmOrderOnPaymentCapturedHandler` et `CancelOrderOnPaymentFailedHandler` rendaient un `Task<Result>` sous une signature `Task` : le `Result` était jeté, et l'outbox — qui ne retient un message que sur **exception** — classait l'échec comme un succès. Ni rejeu, ni lettre morte, ni alerte. Acheteur débité, commande annulée, rien.

Les deux lèvent maintenant, via `IntegrationHandlerFailure.ThrowIfFailed` (dans `BuildingBlocks.Application`, pas dans le projet des contrats d'événements, délibérément sans dépendance).

**Prérequis indispensable, fait dans le même geste** : les deux handlers de commande ont reçu leur garde d'idempotence (commande déjà confirmée ou déjà annulée → succès). Sans elle, chaque rejeu normal d'outbox aurait fini en lettre morte.

---

## 7. §2.5, §2.6, §2.7 — les compensations de la saga

Trois trous, un seul bloc de correction dans `PlaceOrderCommandHandler`.

**La compensation ne se déclenchait que sur échec métier.** Or `TryReserveAsync` **lève** sur conflit de verrou optimiste — le cas le plus attendu en production, documenté ailleurs dans le dépôt. L'exception traversait le handler, les réservations obtenues n'étaient jamais libérées, et la commande n'étant pas persistée, elles restaient rattachées à un identifiant absent de toute table : invisibles du balayeur. Un `try/catch` couvre désormais tout le saga.

**L'engagement de cagnotte n'avait aucune compensation ici.** `ReleaseCreditAsync` n'était appelé nulle part dans ce fichier, et son seul appelant du dépôt exige une commande chargée depuis la base. Tout échec après l'engagement bloquait l'argent du client dans le réservé, définitivement. Le montant réservé est maintenant suivi et rendu par `CompensateAsync`.

**La commande est écrite avant toute consommation définitive.** Un `SaveChanges` a été inséré après `MarkAwaitingPayment`, avant le règlement intégral par cagnotte. Le pire état atteignable n'est plus « client débité, stock parti, aucune ligne de commande » — irrattrapable, puisque balayeur et plafonds ont tous besoin d'un identifiant — mais une commande en attente de paiement que le balayeur sait annuler et compenser.

`CompensateAsync` avale ses propres échecs, ligne par ligne, et utilise `CancellationToken.None` : lever depuis un chemin d'erreur masquerait la faute d'origine et laisserait les libérations suivantes non tentées.

---

## 8. §2.11 et §2.12 — sortir une course de l'échec

**Côté service coursier.** `FailAsync` écrit désormais un message sortant `courier_delivery_failed` dans la même transaction que le changement de statut, avec métrique `dispatch_jobs_failed`. Un point de ramassage en incident compte comme **résolu** pour la condition de départ — sans être marqué « ramassé », ce serait mentir sur ce que le coursier transporte — et un refus explicite `job.nothing_to_deliver` couvre le cas où tous les points sont en incident. Le commentaire qui affirmait « on note et on continue les autres » décrivait exactement ce que le code ne faisait pas.

**Côté marketplace**, la route de réception n'existait pas : le drain aurait reçu des 404 pendant dix tentatives, et la commande serait restée bloquée quand même. `POST /api/delivery/courses/failed` reprend rigoureusement le dispositif de sécurité de `/courses/completed` — le préambule signature+horodatage est extrait en `ReadSignedAsync<T>` partagé, parce que deux vérifications de signature dérivent et que la dérive ne se voit à la lecture d'aucune des deux.

L'expédition est **annotée** sans changer de statut : elle reste `Preparing`, donc le repli transporteur (`MarkShipped`) reste ouvert. Rien n'est crédité. Le vendeur est notifié, l'acheteur non — il ne peut rien en faire.

---

## 9 à 11 — route morte, avis, notifications

**`POST /api/cart/checkout` supprimé**, ainsi que `CheckoutCartCommand` et son handler (aucun autre appelant, src et tests). La route mettait le panier en `CheckedOut` sans créer aucune commande, et rien ne sait rouvrir un panier : l'acheteur perdait tout, définitivement. Les deux documentations mensongères de ce chemin sont corrigées.

**Le signalement d'un avis ne dépublie plus.** Réserver `/flag` aux modérateurs aurait supprimé le seul canal de remontée d'un avis abusif pour corriger un défaut situé ailleurs — et n'aurait pas suffi : le BFF vendeur autorise explicitement un vendeur à signaler les avis de ses propres produits, ce qui était un droit de censure sur ses mauvaises notes. Les trois lectures publiques passent par un prédicat unique `PubliclyVisible` (`Published || Flagged`) ; seul `Rejected` retire. *Contrepartie à connaître : le signalement n'a plus d'effet visible tant qu'aucune file de modération ne l'exploite.*

**Les deux handlers de notification** ont reçu une garde d'idempotence par clé de déduplication, avec **index unique partiel en base** — pas seulement un `SELECT` préalable. Ils étaient les seuls des huit consommateurs de `OrderConfirmed` à n'en avoir aucune, et ils passent avant Settlement, Loyalty et Analytics dans l'ordre de dispatch : une panne en aval rejouait le message entier, jusqu'à dix « Votre paiement a été reçu ».

---

## 12. §3.2 et §3.3 — devise et décimales

**La devise envoyée au prestataire** était celle de la configuration, pas celle de la commande : une commande de 50 EUR devenait une charge de 50 XOF, et le paiement était marqué encaissé pour le montant complet. Les trois passerelles (FedaPay, MTN, Moov) envoient `context.Currency` et **refusent explicitement** une devise qu'elles ne traitent pas — le contrôle est placé avant tout appel réseau, avant même la demande de jeton. Stripe faisait déjà correctement le travail.

**Les décimales sont désormais une propriété de la devise.** `Money` porte la table ISO — 0 pour XOF, XAF, JPY et les treize autres, 2 ailleurs — avec `DecimalsFor`, `IsZeroDecimal` et `Round`. Défaut à 2 pour une devise inconnue, argumenté : sous-arrondir détruit de la valeur réelle, sur-arrondir ne peut plus produire d'encaissement faux depuis que les passerelles refusent une devise non traitée.

La règle est propagée à `Promotion.ComputeDiscount`, `PriceCalculator`, `GrossSplit`, `TaxRule`, `CommissionRule`. Trois tables « zéro décimale » recopiées ailleurs (Stripe, PayPal, `MoneyUnits`) sont supprimées — celle de `MoneyUnits` avait déjà divergé.

Les colonnes `numeric(18,2)` restent valides et inchangées : une valeur à zéro décimale y tient.

---

## Ce qui reste ouvert

**Arrondis non corrigés, hors périmètre des lots parallèles :**

- `Offer.cs` arrondit à **0** décimale avec XOF codé en dur — le défaut miroir : juste en franc CFA, perd les centimes en euro. La correction demande de faire descendre la devise jusqu'à `ComputeBreakdown`.
- `FedaPayPayoutGateway` tronque encore à l'entier (la devise, elle, est correcte).
- `SellerFinanceEndpoints` suppose XOF sur le relevé vendeur.
- `PlaceOrderCommandHandler` appelle `ExtractIncludedTaxAsync` sans transmettre `order.Currency` : le remède complet exige d'ajouter la devise à `ITaxModuleApi`.

**Défauts de l'audit non traités**, par ordre de gravité décroissante :

- §3.5 — le plafond « par commande » des remboursements se contourne par concurrence : lecture, décision, écriture sans verrou, et `ReturnRequest` n'a aucun jeton de concurrence.
- §3.9 — une expédition annulée empêche définitivement la commande d'atteindre « livrée ».
- §3.10 — une part déjà confiée à un transporteur est quand même envoyée au coursier comme point de ramassage.
- §3.11 — le plafond de rémunération de course n'est pas cumulatif sur la commande.
- §3.7 et §3.8 — le litige ne gèle rien malgré trois textes qui l'affirment ; un vendeur ne peut pas en ouvrir un (comparaison entre identifiant de compte et identifiant de boutique, toujours fausse).
- §3.13 — retraits client et coursier « en cours » invisibles de la console.
- §3.14 — fenêtre de survente de quinze minutes entre l'expiration d'une réservation et le passage du balayeur.
- §3.15 et §3.16 — prix figés à l'ajout au panier sans rafraîchissement ; stock vérifié sur la quantité ajoutée, jamais sur le cumul.
- §3.4 — le panier n'est fermé qu'en asynchrone : fenêtre de cinq secondes pour une seconde commande sans clé d'idempotence.

**Compléments naturels des corrections faites :** une file de modération pour exploiter les avis signalés, et une route côté marketplace pour ré-offrir une course échouée (décision produit : nouvelle tentative, remboursement ou litige).

---

## Points à surveiller au premier build

Aucun compilateur ici. Les risques identifiés par relecture :

1. **`ShipmentSummary`** est un record positionnel qui gagne un champ avant `Items`. Seul `ShipmentMapper.ToSummary` le construit — un client qui lirait par position casserait.
2. **`OrderLine.FinalUnitPrice`** passe de colonne à propriété calculée, `LineTotal` fait l'inverse. Le `builder.Ignore` et le `builder.Property` ont été échangés en conséquence.
3. **`CartLineSummary` et `OrderLineSummary`** gardent leurs deux champs ; seule leur sémantique change. Les BFF qui les exposent n'ont pas été touchés au-delà des sommes de remise.
4. **Interpolation avec littéral** dans `Money.ToString` (`$"{Amount.ToString("F" + …)}"`) — légal, mais c'est le point le plus incertain du lot.
5. **`IDispatchMetrics` et `IMarketplaceClient`** gagnent un membre : vérifié qu'aucune autre implémentation n'existe.
6. **`TreatWarningsAsErrors`** est actif : un `using` devenu inutile après les suppressions de routes casserait le build.
