# Documentation — HbaExpress Marketplace (backend)

Cette documentation décrit le **backend** de la marketplace : un **monolithe modulaire** .NET 8
(25 modules métier) exposé par **5 hôtes** (le monolithe `Marketplace.Api` + 4 BFF : Web, Mobile,
Seller, Admin), derrière un reverse-proxy **Traefik**, adossé à **PostgreSQL** (un schéma par module)
et **Redis**.

## Comment lire cette doc

| Fichier | Contenu |
|---------|---------|
| [01-architecture.md](01-architecture.md) | Vue d'ensemble, principes, découpage en couches, cycle de vie d'une requête, arborescence. |
| [02-data-flow.md](02-data-flow.md) | **Comment les données circulent des BFF vers les modules** : CQRS/MediatR, pattern Result, appels synchrones (Contracts), événements de domaine, outbox, événements d'intégration. Exemples de bout en bout. |
| [03-technology-stack.md](03-technology-stack.md) | Chaque technologie : rôle, **pourquoi** ce choix, **comment** elle est configurée (clés de config / variables d'env). |
| [04-building-blocks.md](04-building-blocks.md) | Le noyau partagé (`BuildingBlocks`) documenté méthode par méthode : `Result`/`Error`, messagerie CQRS, behaviors du pipeline, événements de domaine, outbox, `ModuleDbContext`, installeurs. |
| [05-bff-reference.md](05-bff-reference.md) | Les 5 hôtes en détail : groupes d'endpoints, routes, handlers, configuration `Program.cs`. |
| [06-modules-reference.md](06-modules-reference.md) | Les 25 modules : responsabilité, entités, commandes/requêtes, API publique (`Contracts`), schéma DB. |
| [07-seller-flow.md](07-seller-flow.md) | Le **flux vendeur** documenté finement : produits + images (Cloudinary), offres/pricing, commandes, règlement/retraits, KYB. |
| [08-deployment-and-config.md](08-deployment-and-config.md) | Configuration, secrets, environnements, déploiement (renvoie vers `deploy/`). |
| [09-push-notifications.md](09-push-notifications.md) | **Notifications push (FCM)** : chaîne de bout en bout (jetons d'appareil, dispatcher, FcmPushSender), config déploiement (compte de service via Vault), setup Firebase/APNs, apps vendeur (MAUI) et cliente (Flutter), et **guide de dépannage**. |

## Résumé en une page

- **Style architectural** : monolithe modulaire (modular monolith). Un seul processus par hôte, mais
  frontières de modules strictes (schéma DB dédié, pas de JOIN inter-schéma, communication par contrats
  et par événements).
- **CQRS léger** : chaque action métier est une **commande** ou une **requête** MediatR, traitée par un
  **handler** dans la couche Application du module. Les endpoints HTTP ne font que traduire HTTP → message.
- **Pattern Result** : les échecs métier ne sont pas des exceptions mais des `Result.Failure(Error)` typés,
  convertis en codes HTTP par une extension `Match`.
- **Deux modes de communication inter-modules** :
  1. **Synchrone** via des **API de contrat** (`I{Module}ModuleApi`) pour lire une donnée d'un autre module.
  2. **Asynchrone** via **événements d'intégration** écrits dans un **outbox** transactionnel, publiés par un
     `OutboxProcessor` de fond.
- **Persistance** : EF Core + Npgsql, **une base Postgres, un schéma par module**, migrations par module,
  table `outbox_messages` par schéma.
- **Sécurité** : JWT (émis par le module Identity, clé de signature unique partagée par les 5 hôtes),
  BCrypt pour les mots de passe, TOTP (Otp.NET) pour le MFA.

> Le frontend (dashboards Blazor / app mobile MAUI) est hors périmètre de cette doc backend ; voir le
> `README.md` racine et `HbaExpress.MobileDesktop/DISTRIBUTION.md` du dépôt vendeur.
