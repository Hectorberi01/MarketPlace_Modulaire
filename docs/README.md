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
| [11-production-architecture.md](11-production-architecture.md) | **Architecture VPS de production** — *encadré daté en tête : le périmètre livraison (09/10/2026) et la supervision (17/10/2026) ont quitté ce dépôt.* : découpage en 2 hôtes (application / observabilité), dimensionnement chiffré et justifié, limites de ressources, réseau privé et sécurité, base managée et co-location, déploiement zéro-downtime, sauvegardes, chemin de montée en charge, checklist avant mise en prod. |
| [10-observability.md](10-observability.md) | **Observabilité complète** — *encadré daté en tête : la pile Grafana/Prometheus/Loki est sur un VPS dédié depuis le 17/10/2026 ; l'instrumentation applicative, elle, est inchangée.* : instrumentation OpenTelemetry + métriques custom (disponibilité, paiements, marketplace, sécurité), health checks, exporters (node, cAdvisor, Redis), Prometheus + 22 règles d'alerte, Alertmanager (Slack/e-mail), Loki/Tempo, 4 dashboards Grafana provisionnés, intégration dans la console admin, PromQL, **bonnes pratiques de sécurité et de cardinalité**. |
| [12-anatomie-d-une-requete.md](12-anatomie-d-une-requete.md) | 🆕 **Commencez par là si vous découvrez le projet.** Une requête suivie du doigt de l'utilisateur jusqu'à PostgreSQL, et retour — en expliquant **chaque technologie traversée** (Dio, Traefik, Kestrel, MediatR/`ISender`, behaviors, `Result`, EF Core, Redis, outbox) à quelqu'un qui ne les connaît pas. Diagrammes de séquence annotés. |
| [13-diagrammes-de-sequence.md](13-diagrammes-de-sequence.md) | 🆕 **Les parcours métier en diagrammes** : inscription/connexion, fiche produit, **la saga panier → paiement → commande confirmée**, publication d'un produit, retour et remboursement, retrait des gains. Les **trous connus** y sont listés, pas masqués. |
| [14-communication-entre-modules.md](14-communication-entre-modules.md) | 🆕 **Comment 25 modules se parlent sans se coupler** : les 3 canaux autorisés (API de contrat, événement d'intégration via outbox, événement de domaine), la carte réelle des dépendances, l'arbre de décision, et ce que cette architecture **ne vous donne pas**. |

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
- **Observabilité** : OpenTelemetry (traces + métriques) et logs Serilog JSON, exportés vers un **VPS de supervision dédié** (depuis le 17/10/2026). Vide, `Otel:Endpoint` laisse l'instrumentation tourner sans exporter.
  → Promtail → Loki, métriques métier custom (disponibilité, paiements, marketplace, sécurité), 22 règles
  d'alerte → Alertmanager (Slack/e-mail), dashboards Grafana intégrés à la console admin.

> Le frontend (dashboards Blazor / app mobile MAUI) est hors périmètre de cette doc backend ; voir le
> `README.md` racine et `HbaExpress.MobileDesktop/DISTRIBUTION.md` du dépôt vendeur.
