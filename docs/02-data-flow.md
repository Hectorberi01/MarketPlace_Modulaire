# 02 — Circulation des données : des BFF vers les modules

Ce document répond à la question centrale : **comment une requête cliente traverse un BFF, atteint un
module, produit un effet, et déclenche des réactions dans d'autres modules.**

Il existe **trois canaux** de circulation :

1. **BFF → module (écriture/lecture métier)** via **MediatR** (commandes/requêtes) → handler → repository.
2. **Module → module (lecture synchrone)** via une **API de contrat** `I{Module}ModuleApi`.
3. **Module → module (réaction asynchrone)** via **événements de domaine** → **outbox** → **événements
   d'intégration**.

---

## 1. Canal 1 — Du BFF au module (MediatR + Result)

### 1.1 L'endpoint ne fait que traduire

Un endpoint (dans un BFF ou dans `Marketplace.Api`) ne contient **aucune logique métier**. Il :

1. reçoit le HTTP (corps, route, `ClaimsPrincipal`) ;
2. construit une **commande** ou une **requête** ;
3. l'envoie via `ISender.Send(...)` (MediatR) ;
4. convertit le `Result` retourné en réponse HTTP via `Match(...)`.

Exemple (BFF Seller, création de produit) :

```csharp
private static async Task<IResult> CreateWithImagesAsync(
    ClaimsPrincipal user, IFormFileCollection images,
    [FromForm] Guid categoryId, [FromForm] string name, [FromForm] string description,
    ISender sender, ISellerModuleApi sellers, CancellationToken ct)
{
    // 1. Identité de la boutique : TOUJOURS dérivée du JWT, jamais du corps client (anti-usurpation).
    var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
    if (seller is null) return SellerBff.NotASeller();

    // 2. Traduction HTTP → commande.
    var uploads = /* lecture des IFormFile en ProductImageUpload */;
    var result = await sender.Send(new CreateProductWithImagesCommand(
        seller.Id, categoryId, name, description, /* … */ uploads), ct);

    // 3. Result → HTTP.
    return result.Match(r => Results.Ok(r));
}
```

Points clés :
- **`ISellerModuleApi.ResolveSellerAsync`** est un appel **Canal 2** (lecture d'un autre module) pour
  transformer l'`userId` du jeton en `sellerId`. Le client ne fournit jamais son `sellerId`.
- Le BFF **n'accède jamais à une base** directement ; il passe par MediatR ou par une API de contrat.

### 1.2 Le pipeline MediatR

`ISender.Send(command)` traverse les **behaviors** (ordre d'enveloppement) avant d'atteindre le handler :

```
LoggingBehavior   → journalise « Traitement de {Request} » + durée + succès/échec
  ValidationBehavior → exécute les FluentValidation.IValidator<TRequest> ; si échec → Result.Failure(Error.Validation) et court-circuit
    Handler         → la logique métier
```

Les deux behaviors sont contraints à `where TResponse : Result` : ils inspectent le `Result` sans jamais
utiliser d'exception pour le flux métier.

### 1.3 Le handler

Le handler (couche Application du module) orchestre le domaine :

```csharp
public async Task<Result<CreatedProduct>> Handle(CreateProductWithImagesCommand cmd, CancellationToken ct)
{
    // 1. Règles préalables (ex. le vendeur doit être actif) — via Contracts d'un autre module si besoin.
    // 2. Upload des images vers le stockage média (port IMediaStorage).
    // 3. Product.Create(...) → l'agrégat lève un ProductCreatedDomainEvent.
    // 4. _productRepository.AddAsync(product)
    // 5. await _catalogUnitOfWork.SaveChangesAsync(ct)   ← déclenche events + outbox + commit
    return new CreatedProduct(product.Id);
}
```

Le handler retourne un `Result<T>`. Il ne connaît ni HTTP ni MediatR-pipeline ; il ne dépend que
d'abstractions (repositories, ports, `IUnitOfWork`, `I{Autre}ModuleApi`).

### 1.4 Le pattern Result → HTTP

`Result` porte soit une valeur, soit une `Error { Code, Message, Type }`. L'extension `Match` (dans
`Marketplace.Api/Extensions`, réutilisée par les BFF) mappe le `ErrorType` sur un code HTTP :

| `ErrorType`   | HTTP |
|---------------|------|
| `Validation`  | 400  |
| `Unauthorized`| 401  |
| `Forbidden`   | 403  |
| `NotFound`    | 404  |
| `Conflict`    | 409  |
| `Failure`     | 500  |

Ainsi, **aucun `try/catch` métier** dans les endpoints : le `Result` porte l'information et `Match`
produit le bon statut.

---

## 2. Canal 2 — Lecture inter-module synchrone (API de contrat)

Quand un module (ou un BFF) a besoin de **lire** une donnée détenue par un autre module, il n'accède
**jamais** à la base de l'autre. Il appelle l'interface publiée dans le projet `Contracts` de ce module :
`I{Module}ModuleApi`.

Exemples réels :

| Interface (Contracts) | Méthode typique | Utilisée par |
|-----------------------|-----------------|--------------|
| `ISellerModuleApi` | `GetSellerByUserIdAsync(userId)`, `IsActiveSellerAsync(sellerId)` | BFF Seller (résolution boutique), Offers/Catalog (garde « vendeur actif ») |
| `ICatalogModuleApi` | `GetProductAsync(productId)` | BFF Web/Mobile/Seller (enrichir une commande avec le nom/photo produit) |
| `IIdentityModuleApi` | `GetUserAsync(userId)` | BFF Seller/Admin (nom/email du client sur une commande) |
| `IOfferModuleApi` | `GetOffersByProductAsync(productId)` | BFF Web (fiche produit agrégée) |
| `IShippingModuleApi` | `GetShipmentsByOrderAsync(orderId)` | BFF Mobile (suivi de colis) |
| `IPricingModuleApi` | `CalculatePriceAsync(request)` | Cart/Ordering (prix effectif avec promotions) |
| `IPayoutModuleApi` | `SendMobileMoneyPayoutAsync(instruction)` | Settlement (déclencher un retrait FedaPay) |

L'implémentation (`{Module}ModuleApi` dans l'Infrastructure) est enregistrée en DI par l'installer du
module. Comme tout est **in-process**, l'appel est un simple appel de méthode (pas de réseau) mais reste
**derrière un contrat stable** : le jour où le module devient un service distant, seule l'implémentation
change.

> Règle : les API de contrat servent surtout à **lire** (read-models) ou à déclencher une opération bien
> délimitée (ex. un payout). Les **réactions en cascade** entre modules passent plutôt par le Canal 3.

---

## 3. Canal 3 — Réactions asynchrones (domaine → outbox → intégration)

C'est le mécanisme qui fait qu'« une commande passée » finit par créditer un portefeuille vendeur, sans
que le module Ordering ne connaisse le module Settlement.

### 3.1 Événement de domaine (dans le module, synchrone)

Un agrégat lève un **événement de domaine** lorsqu'un fait métier se produit :

```csharp
// Dans l'agrégat Product
Raise(new ProductCreatedDomainEvent(id.Value, sellerId, categoryId, name, slug));
```

Ces événements sont **privés au module**. Au `SaveChangesAsync`, le `ModuleDbContext` les collecte depuis
le `ChangeTracker` et les **dispatch en mémoire** (`IDomainEventDispatcher`) vers les
`IDomainEventHandler<T>` du **même module**.

### 3.2 Traduction en événement d'intégration

Un handler de domaine traduit typiquement un fait privé en **événement d'intégration** public (contrat
inter-module), qu'il **publie** :

```csharp
public Task HandleAsync(ProductCreatedDomainEvent e, CancellationToken ct)
    => _publisher.PublishAsync(new ProductCreatedIntegrationEvent { ProductId = e.ProductId, /* … */ }, ct);
```

`PublishAsync` **n'écrit pas tout de suite en base** : il empile l'événement dans une file scoped
(`IntegrationEventQueue`).

### 3.3 L'outbox : atomicité

Toujours dans le **même `SaveChangesAsync`**, le `ModuleDbContext` :

1. dispatch les événements de domaine (étape 3.1/3.2, qui remplissent la file) ;
2. **draine** la file → insère une ligne `OutboxMessage` (type + JSON) dans la table `outbox_messages`
   **du schéma du module** ;
3. exécute `base.SaveChangesAsync()` → **une seule transaction Postgres** persiste à la fois les agrégats
   *et* les messages d'outbox.

Garantie : soit l'état **et** l'événement sont persistés, soit rien. Pas d'événement fantôme, pas de perte.

### 3.4 Publication différée (OutboxProcessor)

Un `OutboxProcessor<TDbContext>` (BackgroundService, un par module, intervalle ~5 s, lots de 50) :

1. lit les `OutboxMessage` où `ProcessedOnUtc IS NULL` ;
2. désérialise le JSON en `IntegrationEvent` ;
3. le **dispatch** (`IntegrationEventDispatcher`) vers les `IIntegrationEventHandler<T>` enregistrés
   (potentiellement dans **d'autres modules**) ;
4. marque le message `ProcessedOnUtc = now` (ou stocke l'erreur pour re-tentative).

Le consommateur s'exécute dans **son propre scope** ; il peut lire d'autres modules (Canal 2), muter son
propre domaine, et son `SaveChangesAsync` relance à son tour le cycle domaine/outbox.

> `OUTBOX_ENABLED=false` désactive le processor sur un hôte. En pratique, on laisse **un seul** hôte
> traiter l'outbox pour éviter les doublons (les BFF peuvent le désactiver). Le multi-instance nécessiterait
> un verrou `SELECT … FOR UPDATE SKIP LOCKED`.

### 3.5 Idempotence

Les consommateurs d'intégration sont **idempotents** : ils vérifient qu'ils n'ont pas déjà traité
l'événement (ex. `if (await _earningRepository.ExistsForOrderAsync(orderId)) return;`). Indispensable car
un message peut être re-livré après une panne.

---

## 4. Exemple de bout en bout — Création de produit avec détourage image

Illustre Canal 1 + un port d'infrastructure (Cloudinary/R2) + Canal 3.

```
[App vendeur] POST /seller/products/media/process   (multipart : 1 image)
  → BFF Seller: ProcessMediaAsync
     → ISender.Send(ProcessImageCommand(fileName, contentType, bytes))
        → ProcessImageCommandHandler
           → IImageProcessor.RemoveBackgroundWhiteAsync(...)   [Canal port → Cloudinary]
              → upload signé Cloudinary → e_background_removal,b_white → JPEG fond blanc → asset détruit
        ← Result<ProcessedImage>(bytes JPEG)
     ← Results.File(bytes, "image/jpeg")
  ← l'app affiche le récap « avant / après » ; le vendeur valide

[App vendeur] POST /seller/products   (multipart : champs + images retenues)
  → BFF Seller: CreateWithImagesAsync
     → ISellerModuleApi.ResolveSellerAsync(user)                [Canal 2 : userId → sellerId]
     → ISender.Send(CreateProductWithImagesCommand(...))
        → handler : IMediaStorage.UploadManyAsync(...)          [port → Cloudflare R2, stockage final]
           → Product.Create(...) ⇒ Raise(ProductCreatedDomainEvent)
           → SaveChangesAsync :
               • dispatch ProductCreatedDomainEvent (module Catalog)
               • handler → publie ProductCreatedIntegrationEvent → file
               • drain → INSERT catalog.outbox_messages
               • COMMIT (catalog.products + catalog.outbox_messages)
        ← Result<CreatedProduct>
     ← 200 { id }

[Fond] OutboxProcessor<CatalogDbContext>
  → lit catalog.outbox_messages (non traités)
  → dispatch ProductCreatedIntegrationEvent
     → IIntegrationEventHandler dans Search / Inventory / … (indexation, stock initial…)
  → marque ProcessedOnUtc
```

Détails Cloudinary/R2 : voir [07-seller-flow.md](07-seller-flow.md#images).

---

## 5. Exemple — Commande → règlement vendeur (saga par événements)

```
PlaceOrderCommand (Ordering)                → saga : réserve le stock (IInventoryModuleApi.TryReserveAsync),
                                              fige prix (IPricingModuleApi.CalculatePriceAsync) + adresses
   ⇒ OrderPlacedIntegrationEvent            → Payments (initie le paiement), Notifications (email)
Paiement confirmé (admin/PSP)               → OrderConfirmedIntegrationEvent
   ⇒ Settlement.AccrueEarningsOnOrderConfirmedHandler (idempotent)
        → lit la commande (IOrderingModuleApi.GetOrderAsync)
        → calcule commission (Billing.ICommissionModuleApi) + frais provider
        → crédite SellerWallet (pending), écrit WalletTransaction
Expédition livrée (Shipping)                → pending → available sur le wallet
Retrait vendeur validé (Admin/Seller)       → Settlement → IPayoutModuleApi.SendMobileMoneyPayoutAsync
        → Payments → FedaPay (Mobile Money : MTN/Moov/Celtis)
```

Chaque flèche `⇒` est un **événement d'intégration** (Canal 3) ; chaque flèche « lit » est un **Canal 2**.
Aucun module n'appelle en écriture le domaine d'un autre.

---

## 6. Tableau récapitulatif des canaux

| Canal | Quand | Mécanisme | Sync ? | Couplage |
|-------|-------|-----------|--------|----------|
| 1. BFF → module | Toute action cliente | Endpoint → MediatR → handler → repository | Sync | Le BFF connaît les commandes/requêtes du module |
| 2. Module → module (lecture) | Enrichir/valider avec une donnée d'un autre module | `I{Module}ModuleApi` (Contracts) | Sync (in-process) | Contrat d'interface stable |
| 3. Module → module (réaction) | Effet de bord métier en cascade | Domain event → outbox → integration event → handler | Async | Contrat d'événement (aucune référence directe) |
