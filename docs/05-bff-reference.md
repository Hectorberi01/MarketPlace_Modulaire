# 05 — Référence des hôtes (Api + 4 BFF)

Chaque hôte installe **tous** les modules (même `ModuleRegistry`) mais n'expose que les endpoints de sa
surface. Tous partagent la même `Jwt:SigningKey`. Les endpoints sont dans `Endpoints/*.cs` et suivent le
pattern « traducteur HTTP → MediatR » (voir [02-data-flow.md](02-data-flow.md)).

Convention de lecture : `MÉTHODE chemin → HandlerMethod (Message MediatR)`.

---

## 1. `Marketplace.Api` (monolithe de référence)

**Rôle** : surface CQRS directe `/api/{module}/…`, autorité d'authentification, migrations + seed au
démarrage. Communique avec les modules **uniquement par MediatR** (pas d'API de contrat).

**`Program.cs`** : installe tous les modules → MediatR (scan) → pipeline + infrastructure → Redis
(optionnel) → JWT → Swagger → (si `RunMigrationsAtStartup`) migre les 25 DbContext + seed rôles/admin/vendeur
démo → mappe `/health` + tous les `Map{Module}Endpoints()`.

**Seed** (idempotent, dev) : rôles Identity ; admin `admin@marketplace.com` / `Hector01@` ; vendeur démo
`seller@marketplace.com` (boutique « Boutique Démo », payout MtnMomo, amené à l'état `Active`). Surchargable
via `AdminSeed:*` / `SellerSeed:*`.

**Endpoints notables** :
- **Identity** `/api/identity/auth/{register,confirm-email,login,refresh}`, `/account/me` (GET/PUT),
  `/account/me/change-password`, `/account/me/mfa/{setup,confirm,disable}`, `/users/{id}` (+ suspend/roles),
  `/roles` (CRUD + permissions).
- **Catalog** `/api/catalog/products` (CRUD + status + variants + media), `/brands`, `/categories`.
- **Offers** `/api/offers` (get, by-product, by-seller, create, price, status, handling-time).
- **Ordering** `/api/orders` (`POST /` = `PlaceOrderCommand`, saga ; list ; get ; cancel ; confirm-payment
  [admin] ; deliver [admin]).
- Et `Map{Module}Endpoints()` pour Inventory, Pricing, Cart, Payments, Shipping, Reviews, Search,
  Notifications, Billing, Settlement, Returns, Disputes, Messaging, Wishlist, Loyalty, Tax, Marketing,
  Recommendations, Analytics, Fraud, Sellers.

---

## 2. `Marketplace.Bff.Seller` (espace vendeur)

**Rôle** : dashboard vendeur. **L'identité de la boutique est toujours dérivée du JWT** via
`ISellerModuleApi.ResolveSellerAsync`, jamais du client (anti-usurpation). Cache Redis + SignalR (chat).

**Groupes d'endpoints** (préfixe `/seller`) :
- **Auth** `/seller/auth/{login,refresh,logout}`.
- **Dashboard** `GET /seller/dashboard` → agrège commandes + relevé financier + avis (plusieurs modules).
- **Catalog** `/seller/products` : `GET /`, `GET /{id}`, `POST /` (`CreateProductWithImagesCommand`,
  multipart), **`POST /media/process`** (`ProcessImageCommand`, détourage Cloudinary), `PUT /{id}`,
  `PATCH /{id}/status`, `POST /{id}/media` (+ `/media/upload` multipart, `/media/{mediaId}/primary`,
  `PUT /media/order`, `DELETE /media/{mediaId}`), variants (add/update/remove), `DELETE /{id}` ;
  `/seller/categories`, `/seller/brands`.
- **Offers** `/seller/offers` : list, detail, `POST /` (create), `PUT /{id}/price`, `PATCH /{id}/status`,
  `PUT /{id}/handling-time`.
- **Inventory** `/seller/inventory` (stock par sku/location).
- **Orders / Fulfillment / Returns** : lister/traiter ses commandes, expédier, gérer les retours.
- **Finance / Wallet** : relevé, mouvements, retraits (payouts).
- **Reviews / Disputes / Messaging / Notifications** : réponses avis, litiges, chat, notifications.
- **Account / Shop** : profil, KYB, compte payout, personnalisation boutique.

**Communication** : MediatR + API de contrat `ISellerModuleApi`, `ICatalogModuleApi`, `IIdentityModuleApi`,
`IOfferModuleApi`, `IShippingModuleApi`, `IReviewsModuleApi`.

Détail méthode par méthode : [07-seller-flow.md](07-seller-flow.md).

---

## 3. `Marketplace.Bff.Admin` (back-office plateforme)

**Rôle** : gouvernance (rôle Admin/Moderator). CORS restrictif (origines via `Cors:Origins`).

**Groupes d'endpoints** (préfixe `/admin`) :
- **Auth** `/admin/auth/{login,refresh,logout}`.
- **Sellers** `/admin/sellers` : list, get, `POST /{id}/kyb/{approve,reject}`, `GET /kyb/document-url`
  (URL présignée via `IKybDocumentStorage`), `POST /{id}/{activate,suspend}`.
- **Finance** : `/admin/settlement/batches` (list/get), `POST /admin/settlement/run`,
  `POST …/payouts/{payoutId}/paid` ; `/admin/commission-rules` (CRUD + activate/deactivate) ;
  `/admin/invoices` (create, lines, issue, paid, by-seller).
- **Orders / Payments** : `/admin/orders` (list, get, confirm-payment, cancel, deliver) ;
  `/admin/payments` (list, get, by-order, fail, refund).
- **Moderation** : `/admin/reviews/{…}` (flag/reject/restore), `/admin/disputes/{…}` (review/resolve/
  escalate/reopen/messages).
- **Users / Catalog / Products / Wallet / Platform** : administration transverse.

**Communication** : MediatR + `IKybDocumentStorage` (Sellers).

---

## 4. `Marketplace.Bff.Mobile` (app acheteur)

**Rôle** : API compacte pour l'app mobile. JWT + SignalR (chat, jeton en query-string pour WebSocket).

**Groupes** (préfixe `/mobile`) :
- **Auth** `/mobile/auth/{register,login,refresh,logout,password/forgot,password/reset}` ;
  `otp/{request,verify}` → *NotImplemented* (placeholder).
- **Discovery** (recommandations, trending, featured), **Catalog** (browse), **Cart**, **Checkout**.
- **Orders** `/mobile/orders` : list, `GET /{id}` (agrège `IShippingModuleApi` + `ICatalogModuleApi`),
  cancel, `POST /{id}/return` (`RequestReturnCommand`), `GET /{id}/tracking`.
- **Account**, **Messaging**, **Content** (policies/FAQ non modulaires), **Engagement** (notifications,
  wishlist).

**Communication** : MediatR + `ICatalogModuleApi`, `IShippingModuleApi`.

---

## 5. `Marketplace.Bff.Web` (site web acheteur)

**Rôle** : catalogue enrichi/agrégé pour le web/SPA. CORS ouvert en dev.

**Groupes** (préfixe `/web`) :
- **Auth** `/web/auth/{register,login,refresh,logout}` (+ password reset *NotImplemented*).
- **Discovery** (homepage), **Catalog** : `GET /web/products/{id}` (fiche agrégée : `ICatalogModuleApi` +
  `IOfferModuleApi` + note produit), `/reviews`, `/related`, `GET /web/search` (+ `/suggest`),
  `GET /web/shop/{sellerId}` (`ISellerModuleApi`).
- **Cart**, **Checkout**, **Orders**, **Account**, **Messaging**, **Storefront**.

**Communication** : MediatR + `ICatalogModuleApi`, `IOfferModuleApi`, `ISellerModuleApi`,
`IShippingModuleApi`, Search/Recommendations (queries).

---

## Tableau de synthèse

| Hôte | Public | Préfixe | Communication | Spécificités |
|------|--------|---------|---------------|--------------|
| Api | interne / référence | `/api/*` | MediatR pur | migrations + seed, Swagger, autorité auth |
| Seller | vendeurs | `/seller/*` | MediatR + Contracts | boutique dérivée du JWT, SignalR, Cloudinary |
| Admin | admins/modérateurs | `/admin/*` | MediatR + `IKybDocumentStorage` | CORS restrictif, gouvernance/finance |
| Mobile | acheteurs (app) | `/mobile/*` | MediatR + Contracts | payloads compacts, SignalR, OTP placeholder |
| Web | acheteurs (web) | `/web/*` | MediatR + Contracts | fiches agrégées, recherche, CORS dev |
