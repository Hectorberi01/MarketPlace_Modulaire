# 06 — Référence des 25 modules

Chaque module = 4 projets (`Domain` / `Application` / `Infrastructure` / `Contracts`) + son **schéma**
Postgres + sa table `outbox_messages`. Format de chaque fiche : responsabilité · entités clés ·
commandes/requêtes (les « méthodes » du module) · API de contrat exposée · DbContext.

> Les commandes/requêtes listées sont les points d'entrée applicatifs (chacune a un handler qui retourne
> un `Result`). Les modules « event-driven » ont peu de commandes et réagissent surtout à des événements
> d'intégration.

---

## Cœur transactionnel

### Catalog — données maîtresses produit
- **Entités** : `Product` (agrégat), `ProductVariant`, `ProductMedia` (statut de traitement), value objects
  `Sku`, `Slug`, `Dimensions`, `ProductStatus`.
- **Commandes** : `CreateProduct`, `CreateProductWithImages`, `UpdateProduct`, `DeleteProduct`,
  `ChangeProductStatus` ; media : `AddProductMedia`, `RemoveProductMedia`, `ReorderProductMedia`,
  `SetPrimaryProductMedia`, `UploadProductImage`, **`ProcessImage`** (Cloudinary) ; variants :
  `AddProductVariant`, `UpdateProductVariant`, `RemoveProductVariant` ; brands/categories : CRUD +
  publish/unpublish.
- **Requêtes** : `GetProduct`, `ListAllProducts`, `ListProductsBySeller`, `GetBrand`/`ListBrands`,
  `GetCategory`/`ListCategories`.
- **Contrats** : `ICatalogModuleApi.GetProductAsync/GetBrandAsync/GetCategoryAsync`.
- **Ports infra** : `IMediaStorage` (R2), `IImageProcessor` (Cloudinary). **DbContext** : `catalog`.

### Offers — l'offre vendeur (Produit × Vendeur × prix)
- **Entités** : `Offer` (vendeur + variant + prix de base + `FulfillmentType` + `OfferCondition` + `OfferStatus`).
- **Commandes** : `CreateOffer`, `ChangeOfferPrice`, `ChangeOfferStatus`, `SetHandlingTime`.
- **Requêtes** : `GetOffer`, `ListOffersByProduct`, `ListOffersBySeller`.
- **Contrats** : `IOfferModuleApi.GetOfferAsync/GetOffersByProductAsync`. **DbContext** : `offers`.

### Pricing — prix effectif & promotions
- **Entités** : `Promotion` (bornée, ciblée catégorie/segment), `PromotionConditions`, `PriceBreakdown`
  (base + sources de financement), moteur `PriceCalculator`.
- **Commandes/Requêtes** : `CreatePromotion`, `CalculateEffectivePrice`.
- **Contrats** : `IPricingModuleApi.CalculatePriceAsync(request)` → répartition base/remises par source.
  Ne modifie **jamais** le prix de base (c'est Offers). **DbContext** : `pricing`.

### Cart — panier
- **Entités** : `Cart` (mono-devise), `CartItem` (snapshot d'offre + quantité).
- **Commandes/Requêtes** : `AddItemToCart` (+ update/clear), `GetActiveCart`/`GetCart`.
- **Contrats** : `ICartModuleApi.GetActiveCartAsync/GetCartAsync`. Le prix effectif (promos) est calculé au
  checkout par Pricing. **DbContext** : `cart`.

### Ordering — orchestration de commande (saga)
- **Entités** : `Order` (multi-vendeur, prix/adresses **figés**), `OrderLine` (base + remises par source),
  `OrderStatus` (machine à états).
- **Commandes** : `PlaceOrder` (initie la saga : réserve le stock via `IInventoryModuleApi.TryReserveAsync`,
  fige les prix via Pricing) ; cancel, confirm-payment [admin], deliver [admin].
- **Contrats** : `IOrderingModuleApi.GetOrderAsync`. Émet `OrderPlaced/OrderConfirmed…` (événements).
  **DbContext** : `ordering`.

### Payments — encaissement & versement
- **Entités** : `Payment` (statut, réf. provider).
- **Commandes** : `InitiatePayment`.
- **Contrats** : `IPaymentsModuleApi.GetPaymentAsync/GetPaymentByOrderAsync` ;
  **`IPayoutModuleApi.SendMobileMoneyPayoutAsync(instruction)`** (FedaPay Mobile Money). **DbContext** :
  `payments`.

### Settlement — portefeuilles vendeurs & retraits
- **Entités** : `SellerWallet` (soldes *pending*/*available*), `SellerEarning`, `PlatformWallet`,
  `Withdrawal`, `WalletTransaction` (grand-livre immuable), `SettlementBatch`.
- **Requêtes** : `ListSellerPayouts`.
- **Réactions** : consomme `OrderConfirmed` (accrue les gains, idempotent), passe *pending→available* à la
  livraison, déclenche les payouts via `IPayoutModuleApi`. **DbContext** : `settlement`.

### Billing — commissions & factures
- **Entités** : `CommissionRule`, `CommissionResolver`, `Invoice`.
- **Commandes/Requêtes** : commission (règles), invoice (générer/émettre/payer).
- **Contrats** : `ICommissionModuleApi.ComputeCommissionAsync(seller, category, gross, currency)`. Utilisé
  par Settlement pour le split. **DbContext** : `billing`.

### Inventory — stock & réservations
- **Entités** : `InventoryItem` (sku+location+qté), `StockReservation`, `FulfillmentLocation`.
- **Requêtes** : `ListFulfillmentLocations`.
- **Contrats** : `IInventoryModuleApi.GetAvailabilityAsync/IsInStockAsync/TryReserveAsync/
  ReleaseReservationAsync/ConfirmReservationAsync` (étapes de la saga Ordering). **DbContext** : `inventory`.

### Shipping — expéditions
- **Entités** : `Shipment` (par commande/vendeur), `ShipmentItem`.
- **Contrats** : `IShippingModuleApi.GetShipmentsByOrderAsync`. Met à jour l'état commande par événements.
  **DbContext** : `shipping`.

### Returns — retours/remboursements
- **Entités** : `ReturnRequest` (ligne + motif + statut).
- **Commandes** : `RequestReturn`. Coordonne Payments (reversal) et Shipping par événements. **DbContext** :
  `returns`.

---

## Identité & vendeurs

### Identity — comptes, rôles, permissions
- **Entités** : `User`, `Role`, `Permission` (RBAC), `PaymentMethod`, `Address`.
- **Commandes** : `RegisterUser`, `Login`, `Logout`, `RefreshToken`, `ChangePassword`,
  `RequestPasswordReset`/`ResetPassword`, `ConfirmEmail`, `UpdateUserProfile`, `Suspend`/`Reactivate`,
  MFA (`BeginMfaSetup`/`ConfirmMfa`/`DisableMfa`) ; rôles (CRUD + `AssignRole`/`RemoveRole`/
  `SetRolePermissions`).
- **Requêtes** : `GetUser`, `ListUsers`, `GetRole`, `ListRoles`.
- **Contrats** : `IIdentityModuleApi.GetUserAsync/GetUserByEmailAsync`. Autorité JWT. **DbContext** :
  `identity`.

### Sellers — onboarding, KYB, payout
- **Entités** : `Seller` (agrégat : boutique, statut KYB, `SellerStatus`), `KybDocument`, `PayoutAccount`
  (MSISDN + `PayoutProvider` : MtnMomo/MoovMoney/Celtis).
- **Commandes** : `RegisterSeller`, `AddKybDocument`/`RemoveKybDocument`, `ApproveKyb`/`RejectKyb`,
  `SetPayoutAccount`, `UpdateSellerProfile`, `Activate`/`Suspend`.
- **Requêtes** : `GetSeller`, `GetSellerByUser`, `ListSellers`.
- **Contrats** : `ISellerModuleApi.GetSellerAsync/GetSellerByUserIdAsync/IsActiveSellerAsync`.
- **Ports** : `IKybDocumentStorage` (R2 privé + URL présignées). **DbContext** : `sellers`.

---

## Découverte & contenu

### Search — recherche produit
- **Entités** : `SearchDocument` (produit+offre dénormalisé). Réplique lecture-seule alimentée par les
  événements Catalog/Offers.
- **Requêtes** : `SearchProducts` (+ suggest). **DbContext** : `search`.

### Recommendations — suggestions
- **Entités** : `Recommendation` (item + raison + score). Piloté par signaux d'achat/avis. **DbContext** :
  `recommendations`.

### Reviews — avis & notes
- **Entités** : `Review` (produit + acheteur + note + texte, réponse vendeur).
- **Commandes** : `SubmitReview`, `ReplyToReview`.
- **Contrats** : `IReviewsModuleApi.GetProductRatingAsync` (expose la note agrégée, pas les avis bruts).
  **DbContext** : `reviews`.

### Marketing — contenu & campagnes
- **Entités** : `Campaign`, `ContentBlock` (CMS bannières/hero). Découplé de Pricing. **DbContext** :
  `marketing`.

### Messaging — conversations acheteur↔vendeur
- **Entités** : `Conversation`, `Message`, `ConversationParticipant`. Temps réel via SignalR côté BFF.
  **DbContext** : `messaging`.

### Notifications — alertes multi-canal
- **Entités** : `Notification`. Consomme des événements (commande passée, paiement, expédition) et
  dispatche email/SMS/in-app. **DbContext** : `notifications`.

---

## Engagement & transverses

### Wishlist — listes de souhaits
- **Entités** : `WishlistAggregate`. **Requêtes** : `GetMyWishlist`. **DbContext** : `wishlist`.

### Loyalty — points/fidélité
- **Entités** : `LoyaltyAccount`. Piloté par événements (commandes/avis). **DbContext** : `loyalty`.

### Tax — calcul de taxes
- **Entités** : `TaxRule`, moteur `TaxResolver`.
- **Contrats** : `ITaxModuleApi.ComputeTaxAsync(jurisdiction, category, amount)`. Calcul sans état.
  **DbContext** : `tax`.

### Disputes — litiges
- **Entités** : `Dispute` (machine à états), `DisputeMessage`.
- **Commandes** : cycle de vie (open/escalate/resolve/close) + messages. **DbContext** : `disputes`.

### Fraud — évaluation de risque
- **Entités** : `RiskAssessment`, moteur `RiskScorer`.
- **Contrats** : `IFraudModuleApi.GetDecisionAsync(subjectType, subjectId)` → `Allow`/`Review`/`Block`.
  **DbContext** : `fraud`.

### Analytics — métriques
- **Entités** : `MetricSnapshot`, `ProcessedOrder`. Ingestion via événements de domaine. **DbContext** :
  `analytics`.

---

## Vue d'ensemble

| Module | API de contrat | Rôle en une ligne |
|--------|----------------|-------------------|
| Catalog | ICatalogModuleApi | données produit maîtresses |
| Offers | IOfferModuleApi | offre vendeur (prix de base) |
| Pricing | IPricingModuleApi | prix effectif + promotions |
| Cart | ICartModuleApi | panier |
| Ordering | IOrderingModuleApi | saga de commande |
| Payments | IPaymentsModuleApi + IPayoutModuleApi | encaissement + payout |
| Settlement | (événements) | portefeuilles + retraits |
| Billing | ICommissionModuleApi | commissions + factures |
| Inventory | IInventoryModuleApi | stock + réservations |
| Shipping | IShippingModuleApi | expéditions |
| Returns | (événements) | retours/remboursements |
| Identity | IIdentityModuleApi | comptes/rôles/JWT |
| Sellers | ISellerModuleApi | onboarding/KYB/payout |
| Search | (événements) | recherche |
| Recommendations | (événements) | suggestions |
| Reviews | IReviewsModuleApi | avis/notes |
| Marketing | — | contenu/campagnes |
| Messaging | — | chat |
| Notifications | — | alertes |
| Wishlist | — | souhaits |
| Loyalty | — | fidélité |
| Tax | ITaxModuleApi | taxes |
| Disputes | — | litiges |
| Fraud | IFraudModuleApi | risque |
| Analytics | — | métriques |
