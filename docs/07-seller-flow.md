# 07 — Flux vendeur en détail (méthode par méthode)

Cette page documente finement la chaîne **BFF Seller → modules** pour les parcours vendeur, y compris les
intégrations externes (Cloudinary, R2, FedaPay) construites récemment.

---

## 1. Produit + images (détourage Cloudinary → fond blanc) {#images}

### 1.1 Traitement d'une image (avant création)

But : montrer au vendeur un **récap avant/après** (fond détouré) avant de valider.

**Endpoint** — `POST /seller/products/media/process` (multipart, champ `image`) →
`SellerCatalogEndpoints.ProcessMediaAsync` :
1. `SellerBff.ResolveSellerAsync(user, sellers)` — garde « est un vendeur » (Canal 2).
2. lit le `IFormFile` en `byte[]`.
3. `sender.Send(new ProcessImageCommand(fileName, contentType, bytes))`.
4. `result.Match(r => Results.File(r.Content, r.ContentType))` — renvoie le **JPEG traité**.

**Commande** — `ProcessImageCommand` (module Catalog) / `ProcessImageCommandHandler` : délègue à
`IImageProcessor.RemoveBackgroundWhiteAsync(fileName, contentType, content)`. Ne persiste rien (pas de
produit encore).

**Adaptateur** — `CloudinaryImageProcessor` (implémente `IImageProcessor`) :
- `RemoveBackgroundWhiteAsync(...)` :
  1. `UploadAsync(...)` — upload signé de l'original vers Cloudinary. **Auth passée en query-string**
     (`api_key`, `timestamp`, `signature`) car les champs multipart texte n'étaient pas reconnus (l'upload
     était traité comme *unsigned*). Retourne `(publicId, version, error?)`.
  2. Construit l'URL de livraison `…/image/upload/e_background_removal,b_white/v{version}/{publicId}.jpg`.
  3. **Boucle de récupération** : `GET` avec re-tentative tant que Cloudinary renvoie **HTTP 423**
     (traitement IA asynchrone en cours), jusqu'à `MaxWaitSeconds`.
  4. `DestroyAsync(publicId)` — supprime l'asset Cloudinary (best-effort) : Cloudinary ne sert **qu'au
     traitement**, l'image finale ira dans R2.
  5. retourne `ProcessedImage(bytes JPEG, "image/jpeg")`.
- `Sign(paramsToSign)` — signature Cloudinary = **SHA1 hex** de `params + api_secret`.
- Repli : non configuré → `NullImageProcessor` (renvoie l'original) ; erreur → `Result.Failure` (l'UI
  retombe sur l'originale, bouton « Réessayer »).

> Prérequis compte : add-on **AI Background Removal** actif et **strict transformations désactivé** (sinon
> la livraison `e_background_removal` est bloquée). Config : `Media:Cloudinary:*` / env `CLOUDINARY_*`.

### 1.2 Création du produit avec images retenues

**Endpoint** — `POST /seller/products` (multipart) → `SellerCatalogEndpoints.CreateWithImagesAsync` :
1. `ResolveSellerAsync` (boutique depuis le JWT).
2. lit les `IFormFile` → `List<ProductImageUpload>` ; parse `attributesJson`, `tags`.
3. `sender.Send(new CreateProductWithImagesCommand(sellerId, categoryId, name, description, brandId, gtin,
   ean, productGroupId, attributes, tags, uploads))`.

**Commande** — `CreateProductWithImagesCommand` (Catalog) :
- `IMediaStorage.UploadManyAsync(...)` — **stockage final dans Cloudflare R2** (les octets déjà détourés).
  `CloudflareR2MediaStorage` signe en **AWS SigV4** (PutObject), renvoie l'URL publique.
- `Product.Create(...)` — l'agrégat valide et lève `ProductCreatedDomainEvent` ; la 1re image = principale.
- `IProductRepository.AddAsync` + `ICatalogUnitOfWork.SaveChangesAsync` → events + outbox + commit
  (voir [02-data-flow.md](02-data-flow.md#4-exemple-de-bout-en-bout--création-de-produit-avec-détourage-image)).

### 1.3 Gestion des images d'un produit existant

Endpoints `/{id}/media…` : `AddProductMedia` (par URL), `UploadProductImage` (multipart — **passe aussi par
le détourage** côté client puis stocke en R2), `SetPrimaryProductMedia`, `ReorderProductMedia`,
`RemoveProductMedia` (supprime aussi le fichier R2 via `IMediaStorage.DeleteAsync`).

---

## 2. Offres & décomposition de prix (pricing breakdown)

- `POST /seller/offers` → `CreateOfferCommand` (Offers) : crée l'offre (produit × vendeur × **prix de
  base** × condition × mode de livraison). Garde « vendeur actif » via `ISellerModuleApi.IsActiveSellerAsync`.
- `PUT /seller/offers/{id}/price` → `ChangeOfferPriceCommand` ; `PATCH …/status` → `ChangeOfferStatusCommand` ;
  `PUT …/handling-time` → `SetHandlingTimeCommand`.
- Au checkout, **Pricing** (`IPricingModuleApi.CalculatePriceAsync`) empile les promotions sur le prix de
  base et renvoie un `PriceBreakdown` (base + remises par **source de financement** : vendeur / plateforme).
  Ordering fige ce breakdown par ligne (`OrderLine`). C'est ce qui permet, au règlement, d'imputer chaque
  remise à la bonne partie.

---

## 3. Commandes vendeur

- `GET /seller/orders` → `ListOrdersBySellerQuery`, enrichi via `ISellerModuleApi` + `IIdentityModuleApi`
  (nom/email client).
- `GET /seller/orders/{id}` → `GetOrderQuery` + `ICatalogModuleApi.GetProductAsync` (nom/photo produit).
- Traitement/expédition : passe par Fulfillment/Shipping (marque l'expédition, ce qui fait progresser la
  commande par événements).

---

## 4. Règlement & retraits (Settlement + Payments/FedaPay)

Chaîne événementielle (voir aussi [02-data-flow.md](02-data-flow.md#5-exemple--commande--règlement-vendeur-saga-par-événements)) :

1. **`OrderConfirmed`** ⇒ `Settlement.AccrueEarningsOnOrderConfirmedHandler` (idempotent) :
   - lit la commande (`IOrderingModuleApi.GetOrderAsync`) ;
   - commission via `Billing.ICommissionModuleApi.ComputeCommissionAsync` + frais provider ;
   - crédite `SellerWallet` en **pending**, écrit un `WalletTransaction`.
2. **Livraison** ⇒ le solde passe **pending → available**.
3. **Retrait** (validé par l'admin ou demandé par le vendeur) ⇒ Settlement appelle
   `IPayoutModuleApi.SendMobileMoneyPayoutAsync(instruction)`.

**Payout FedaPay** (`IPayoutModuleApi`) : le **`mode`** Mobile Money est **dérivé de l'opérateur du
vendeur** (`PayoutAccount.Provider`) :

| Provider vendeur | `mode` FedaPay |
|------------------|----------------|
| `MtnMomo` | `mtn_open` |
| `MoovMoney` | `moov` |
| `Celtis` | `sbin` |

Le numéro est mis au format **E.164**. `Payments:FedaPay:EnablePayouts=false` ⇒ payout **simulé** ;
`true` ⇒ versement réel (nécessite l'activation des payouts côté compte FedaPay).

---

## 5. Onboarding & KYB

- `POST /api/sellers` (`RegisterSellerCommand`) → crée la boutique (statut initial).
- `POST /api/sellers/me/kyb` (`AddKybDocumentCommand`) → dépose une pièce (stockée en **R2 privé** via
  `IKybDocumentStorage`).
- **Admin** : `POST /admin/sellers/{id}/kyb/approve|reject` (`ApproveKyb`/`RejectKyb`),
  `GET /admin/sellers/kyb/document-url` (URL présignée de lecture), puis
  `POST /admin/sellers/{id}/activate` (`ActivateSellerCommand`) → la boutique passe **Active** et peut
  publier des offres. `IsActiveSellerAsync` garde toutes les créations produit/offre.

---

## 6. Points de sécurité récurrents

- **Identité boutique = JWT**, jamais le corps client (`ResolveSellerAsync`).
- **Anti-IDOR** : chaque mutation par `id` vérifie l'appartenance au vendeur du jeton.
- **Secrets hors dépôt** : Cloudinary/R2/FedaPay via `.env`/vault ; jamais dans un fichier suivi.
- **Repli gracieux** : si un service externe (Cloudinary) échoue, le parcours continue avec l'originale.
