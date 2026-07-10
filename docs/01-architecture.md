# 01 — Architecture

## 1. Style : monolithe modulaire

Le backend est un **monolithe modulaire** : tout tourne dans un seul processus par hôte (déploiement
et transactions simples d'un monolithe), mais le code est découpé en **25 modules métier** aux
frontières strictes (isolation et évolutivité d'un système à services). Chaque module est une « tranche
verticale » complète (domaine + application + infrastructure + contrat public).

Pourquoi ce choix : au démarrage d'une marketplace, la complexité opérationnelle de microservices
(réseau, cohérence distribuée, déploiements multiples) n'est pas justifiée. Le modular monolith garde
des frontières nettes pour pouvoir **extraire un module en service plus tard** sans réécrire le métier —
les événements d'intégration et les API de contrat sont déjà les seuls points de couplage.

## 2. Les cinq hôtes

Un même ensemble de modules est composé in-process dans **cinq exécutables** :

```
                         ┌──────────── Traefik (edge :80/:443) ───────────┐
   navigateur / app ────▶│  api.        → Marketplace.Api    (monolithe)  │
                         │  web.        → Bff.Web            (acheteur web)│──▶ PostgreSQL
                         │  m. / mobile → Bff.Mobile         (app mobile)  │    (1 base, 25 schémas)
                         │  seller.     → Bff.Seller         (vendeurs)    │──▶ Redis (cache + SignalR)
                         │  admin.      → Bff.Admin          (back-office) │
                         └────────────────────────────────────────────────┘
```

- **`Marketplace.Api`** (dossier `src/Bootstrap`) : le monolithe « de référence ». Il installe **tous**
  les modules, expose les endpoints `/api/{module}/...` (surface CQRS directe), fait autorité pour
  l'authentification et exécute les migrations + le *seed* au démarrage.
- **Les 4 BFF** (`src/Bff/Marketplace.Bff.{Web,Mobile,Seller,Admin}`) : chaque *Backend-For-Frontend*
  est taillé pour **une surface cliente**. Il installe les mêmes modules mais n'expose que les endpoints
  utiles à son public, avec des payloads adaptés (compacts pour le mobile, agrégés pour le web, etc.),
  et **agrège** au besoin plusieurs modules en une seule réponse.

Chaque hôte est indépendant : on peut scaler le BFF Mobile sans toucher à l'Admin. Ils partagent la
même base Postgres et la **même clé de signature JWT** (indispensable pour que le jeton émis par l'un
soit accepté par les autres).

## 3. Couches d'un module

Chaque module suit la même structure en 4 projets (Clean Architecture / Onion) :

```
Marketplace.Modules.{Module}.Domain          ← entités, agrégats, value objects, événements de domaine, interfaces repository
Marketplace.Modules.{Module}.Application      ← commandes/requêtes + handlers, validators, abstractions (ports)
Marketplace.Modules.{Module}.Infrastructure   ← DbContext, repositories EF, adaptateurs externes, installer
Marketplace.Modules.{Module}.Contracts        ← API publique consommée par les autres modules et les BFF (I{Module}ModuleApi, DTO, événements d'intégration)
```

Règle de dépendances (vérifiée par des **tests d'architecture** NetArchTest) :

- `Domain` ne dépend de rien (hors `BuildingBlocks.Domain`).
- `Application` dépend de `Domain` + `BuildingBlocks.Application`.
- `Infrastructure` dépend de `Application` + `Domain`.
- **Aucun module ne référence l'`Infrastructure`/`Domain`/`Application` d'un autre module.** Le seul point
  d'entrée autorisé vers un autre module est son projet **`Contracts`** (interfaces + DTO + événements).

## 4. Le noyau partagé : `BuildingBlocks`

Quatre projets transverses fournissent la « plomberie » commune (détaillés dans
[04-building-blocks.md](04-building-blocks.md)) :

- **`BuildingBlocks.Domain`** : `Entity<TId>`, `AggregateRoot<TId>`, `Result`/`Result<T>`/`Error`,
  `IDomainEvent`.
- **`BuildingBlocks.Application`** : interfaces CQRS (`ICommand`, `IQuery`, handlers), behaviors du
  pipeline MediatR (validation, logging), abstractions d'événements.
- **`BuildingBlocks.Infrastructure`** : `ModuleDbContext` de base, dispatchers d'événements, outbox +
  `OutboxProcessor`, cache.
- **`BuildingBlocks.IntegrationEvents`** : `IntegrationEvent`, `IIntegrationEventHandler`,
  `IIntegrationEventPublisher` — le contrat d'événement inter-module.

## 5. Cycle de vie d'une requête (vue macro)

```
HTTP → Endpoint (BFF ou Api)
     → traduit le corps HTTP en Command/Query
     → ISender.Send(message)               [MediatR]
        → LoggingBehavior                   [entrée/sortie + durée]
        → ValidationBehavior                [FluentValidation ; court-circuite si invalide]
        → Handler (couche Application)      [charge l'agrégat, applique la règle métier]
           → IUnitOfWork.SaveChangesAsync()
              → ModuleDbContext.SaveChangesAsync()
                 1. dispatch des événements de domaine (in-process, même module)
                 2. « drain » des événements d'intégration produits → table outbox
                 3. COMMIT unique (agrégats + outbox dans la même transaction)
        ← Result<T>
     ← Match(result) → IResult (200/201/400/404/409/500 selon Error.Type)
HTTP ← réponse

En tâche de fond (toutes les ~5 s) :
OutboxProcessor → lit les outbox non traités → désérialise → IIntegrationEventHandler d'autres modules
```

Voir [02-data-flow.md](02-data-flow.md) pour le détail et des exemples concrets.

## 6. Arborescence du dépôt

```
marketPlace/
├─ src/
│  ├─ Bootstrap/Marketplace.Api/           ← hôte monolithe (Program.cs, endpoints /api, ModuleRegistry, seed)
│  ├─ Bff/Marketplace.Bff.{Web,Mobile,Seller,Admin}/  ← 4 BFF (Endpoints/, Program.cs)
│  ├─ BuildingBlocks/                       ← noyau partagé (Domain, Application, Infrastructure, IntegrationEvents)
│  └─ Modules/{Analytics…Wishlist}/         ← 25 modules × 4 projets (Domain/Application/Infrastructure/Contracts)
├─ tests/                                   ← xUnit par module + Architecture.Tests (NetArchTest)
├─ deploy/                                  ← Dockerfile, docker-compose.*, Traefik, Ansible (voir deploy/README.md)
├─ docs/                                    ← cette documentation
├─ Directory.Packages.props                ← gestion centralisée des versions de packages (CPM)
└─ Directory.Build.props                   ← propriétés MSBuild communes (TargetFramework, Nullable, warnings)
```

## 7. Principes directeurs (à retenir)

1. **L'échec métier est une valeur, pas une exception** — `Result.Failure(Error)` typé.
2. **Sûreté transactionnelle** — l'état et les événements d'intégration sont écrits dans la **même
   transaction** (outbox), garantissant qu'aucun événement n'est perdu ni émis sans que l'état soit
   persisté.
3. **Isolation des modules** — un schéma par module, pas de requête cross-schéma ; on lit un autre module
   via son `Contracts`.
4. **Découplage asynchrone** — les réactions inter-modules passent par événements d'intégration
   (outbox → processor), pas par appels directs en écriture.
5. **CQRS lisible** — commandes/requêtes explicites ; aucun métier dans les endpoints.
