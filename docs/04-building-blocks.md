# 04 — Noyau partagé (`BuildingBlocks`) : référence

Quatre projets fournissent la plomberie commune à tous les modules. Cette page documente les abstractions
clés **et leurs méthodes**.

> Les signatures ci-dessous reflètent l'usage réel observé dans le code ; certaines sont représentées de
> façon simplifiée. En cas de doute, la source fait foi (`src/BuildingBlocks/…`).

---

## 1. `BuildingBlocks.Domain`

### 1.1 `Result` / `Result<TValue>`

Conteneur immuable succès/échec, utilisé comme **type de retour de toute opération métier**.

| Membre | Rôle |
|--------|------|
| `bool IsSuccess` / `bool IsFailure` | État. |
| `Error Error` | L'erreur si échec (`Error.None` sinon). |
| `TValue Value` (sur `Result<T>`) | La valeur si succès ; lève si on la lit sur un échec. |
| `static Result Success()` | Succès sans valeur (commande). |
| `static Result<T> Success<T>(T value)` | Succès avec valeur. |
| `static Result Failure(Error error)` | Échec. |
| `static Result<T> Failure<T>(Error error)` | Échec typé. |
| **conversions implicites** | `T ⇒ Result<T>` (retourner directement la valeur) et `Error ⇒ Result<T>` (retourner directement une erreur). C'est ce qui permet d'écrire `return product;` ou `return Error.NotFound(...);` dans un handler. |

### 1.2 `Error` et `ErrorType`

```csharp
record Error(string Code, string Message, ErrorType Type = ErrorType.Failure);
enum ErrorType { Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden }
```

Fabriques : `Error.Failure(code,msg)`, `Error.Validation(...)`, `Error.NotFound(...)`, `Error.Conflict(...)`,
`Error.Unauthorized(...)`, `Error.Forbidden(...)`. Le `Type` pilote le code HTTP (voir
[02-data-flow.md](02-data-flow.md#14-le-pattern-result--http)).

### 1.3 `Entity<TId>` / `AggregateRoot<TId>`

- `Entity<TId>` : identité + égalité par identité.
- `AggregateRoot<TId> : Entity<TId>, IHasDomainEvents` : racine d'agrégat.
  - `protected void Raise(IDomainEvent e)` : enregistre un événement de domaine.
  - `IReadOnlyCollection<IDomainEvent> DomainEvents` : événements en attente.
  - `void ClearDomainEvents()` : vidés après dispatch.
- `IHasDomainEvents` : marqueur non générique permettant au `ChangeTracker` EF de trouver les agrégats
  porteurs d'événements sans connaître leur type d'`Id`.

### 1.4 `IDomainEvent`

```csharp
interface IDomainEvent { Guid EventId { get; } DateTime OccurredOnUtc { get; } }
abstract record DomainEvent : IDomainEvent;  // remplit EventId/OccurredOnUtc
```

---

## 2. `BuildingBlocks.Application`

### 2.1 Messagerie CQRS (au-dessus de MediatR)

```csharp
interface ICommand              : IRequest<Result>;
interface ICommand<TResponse>   : IRequest<Result<TResponse>>;
interface IQuery<TResponse>     : IRequest<Result<TResponse>>;

interface ICommandHandler<TCommand>              : IRequestHandler<TCommand, Result>;
interface ICommandHandler<TCommand, TResponse>   : IRequestHandler<TCommand, Result<TResponse>>;
interface IQueryHandler<TQuery, TResponse>       : IRequestHandler<TQuery, Result<TResponse>>;
```

Conséquence : **tout handler retourne un `Result`**. On distingue commandes (mutation) et requêtes
(lecture) par convention de nommage et d'interface.

### 2.2 Behaviors du pipeline

Enregistrés par `AddBuildingBlocksPipeline()` (ordre = enveloppement) :

- **`LoggingBehavior<TRequest,TResponse>`** : loggue `Traitement de {Request}`, mesure la durée, et
  distingue succès (`LogInformation`) vs échec métier (`LogWarning` avec `Error.Code`/`Message`). C'est ce
  behavior qui produit les lignes `… en échec (image.process.upload_failed) …` vues en debug.
- **`ValidationBehavior<TRequest,TResponse>`** : exécute tous les `IValidator<TRequest>` (FluentValidation)
  du module ; s'il y a des échecs, retourne `Result.Failure(Error.Validation("validation.failed", …))`
  **sans appeler le handler**. Contrainte `where TResponse : Result`.

### 2.3 Abstractions d'événements

- `IDomainEventHandler<TDomainEvent>` : `Task HandleAsync(TDomainEvent e, CancellationToken)`.
- `IDomainEventDispatcher` : `Task DispatchAsync(IEnumerable<IDomainEvent>, CancellationToken)`.
- `IUnitOfWork` : `Task<int> SaveChangesAsync(CancellationToken)` — chaque module en dérive une interface
  dédiée (`ICatalogUnitOfWork`, `ISettlementUnitOfWork`…) pour éviter les collisions de DI ; elle pointe
  toujours sur le `DbContext` du module.

---

## 3. `BuildingBlocks.IntegrationEvents`

Contrat d'événement **inter-module** (le seul « langage » public entre modules pour les réactions).

```csharp
abstract record IntegrationEvent { Guid Id; DateTime OccurredOnUtc; }
interface IIntegrationEventHandler<TEvent> { Task HandleAsync(TEvent e, CancellationToken); }
interface IIntegrationEventPublisher       { Task PublishAsync(IntegrationEvent e, CancellationToken); }
```

- Un module **publie** un `IntegrationEvent` (via `IIntegrationEventPublisher`).
- Un autre module **consomme** en implémentant `IIntegrationEventHandler<TEvent>` (enregistré dans son
  installer). Le dispatch réel a lieu depuis l'`OutboxProcessor`.

---

## 4. `BuildingBlocks.Infrastructure`

### 4.1 `ModuleDbContext` (base de tous les DbContext)

Cœur de la sûreté transactionnelle. `SaveChangesAsync` fait, dans l'ordre :

1. **`DispatchDomainEventsAsync`** — récupère les agrégats `IHasDomainEvents` du `ChangeTracker`,
   collecte leurs `DomainEvents`, les vide, puis `IDomainEventDispatcher.DispatchAsync(...)`. Les handlers
   de domaine peuvent **publier** des événements d'intégration (mis en file).
2. **`DrainIntegrationEventsToOutbox`** — vide la file `IntegrationEventQueue` et insère un
   `OutboxMessage { Type, Content(JSON), OccurredOnUtc }` par événement.
3. **`base.SaveChangesAsync()`** — **une seule transaction** : agrégats + `outbox_messages`.

Autres membres :
- `protected abstract string Schema { get; }` — chaque module fixe son schéma (`"catalog"`, `"ordering"`…).
- `OnModelCreating` — `HasDefaultSchema(Schema)` + applique la config outbox + les configs du module.
- `DbSet<OutboxMessage> OutboxMessages` (implémente `IOutboxDbContext`).

### 4.2 Événements de domaine — dispatcher

`DomainEventDispatcher` résout par réflexion `IDomainEventHandler<TConcret>` dans le conteneur et appelle
`HandleAsync`. **In-process, synchrone, intra-module** — un événement de domaine ne quitte jamais son module.

### 4.3 Outbox

- **`OutboxMessage`** : `Id`, `Type` (nom stable `FullName, AssemblyName`), `Content` (`jsonb`),
  `OccurredOnUtc`, `ProcessedOnUtc?`, `Error?`. Indexé sur `ProcessedOnUtc` (ne poller que les non-traités).
- **`IntegrationEventQueue`** (scoped/requête) : `PublishAsync` empile (aucune écriture DB) ; `DequeueAll`
  vidé par le `ModuleDbContext`. Enregistrée aussi comme `IIntegrationEventPublisher`.
- **`OutboxProcessor<TDbContext>`** (BackgroundService) : boucle toutes les ~5 s, lot de 50 ; désérialise,
  `IntegrationEventDispatcher.DispatchAsync`, marque `ProcessedOnUtc` ou stocke `Error`.
- **`AddOutboxProcessor<TDbContext>()`** : enregistre le hosted service, sauf si `OUTBOX_ENABLED=false`.

### 4.4 `IntegrationEventDispatcher`

Même principe que le dispatcher de domaine, mais pour les `IIntegrationEventHandler<T>`, appelé depuis
l'`OutboxProcessor`. C'est le point où l'on brancherait un vrai bus (Kafka/RabbitMQ) plus tard : le contrat
`IIntegrationEventHandler` resterait identique.

### 4.5 Cache

- `AddBuildingBlocksInfrastructure()` enregistre un `IDistributedCache` mémoire par défaut + un
  `ICacheService` (`DistributedCacheService`).
- Si `Redis:ConnectionString` est présent, `AddStackExchangeRedisCache` remplace le cache mémoire par Redis
  (préfixe de clés `Redis:InstanceName`, défaut `"marketplace:"`).

---

## 5. Pattern d'installation d'un module

Chaque module Infrastructure expose un **installer** (`IModuleInstaller`) :

```csharp
interface IModuleInstaller {
    string ModuleName { get; }
    Assembly ApplicationAssembly { get; }               // pour le scan MediatR
    void Install(IServiceCollection services, IConfiguration configuration);
}
```

`Install(...)` enregistre typiquement :
- le `DbContext` (`AddDbContext<XDbContext>(UseNpgsql(connectionString, MigrationsHistoryTable(..., schema)))`) ;
- l'`I{X}UnitOfWork` → le `DbContext` ;
- les repositories (`IProductRepository → ProductRepository`, …) ;
- l'**API de contrat** (`ICatalogModuleApi → CatalogModuleApi`) ;
- les adaptateurs externes (ex. `IMediaStorage → CloudflareR2MediaStorage`, `IImageProcessor → CloudinaryImageProcessor`) ;
- les `IDomainEventHandler<…>` du module ;
- `AddValidatorsFromAssembly(ApplicationAssembly)` (FluentValidation) ;
- `AddOutboxProcessor<XDbContext>()`.

`ModuleRegistry.Installers` (dans `Marketplace.Api/Modules`) liste les 25 installers. Au démarrage :

```csharp
foreach (var m in ModuleRegistry.Installers) m.Install(services, configuration);   // 1) modules
services.AddMediatR(cfg => /* scan de chaque ApplicationAssembly distinct */);      // 2) MediatR
services.AddBuildingBlocksPipeline();                                               // 3) behaviors
services.AddBuildingBlocksInfrastructure();                                         // 3) events/outbox/cache
```

Chaque BFF fait la même chose (même `ModuleRegistry`), puis n'expose que **ses** endpoints.
