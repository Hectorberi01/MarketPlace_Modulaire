# 03 — Stack technique : quoi, pourquoi, comment configuré

Versions centralisées dans `Directory.Packages.props` (Central Package Management : une seule source de
vérité pour les versions). Runtime cible : **.NET 8** (`Directory.Build.props`).

---

## Plateforme & langage

### .NET 8 / C# 12
- **Rôle** : runtime et langage de tout le backend.
- **Pourquoi** : LTS, performances, `record`/pattern matching (idéal pour DDD + Result), Minimal APIs.
- **Config** : `Directory.Build.props` (TargetFramework `net8.0`, `Nullable=enable`, `ImplicitUsings`,
  warnings traités strictement — d'où l'importance des annotations de nullité).

### ASP.NET Core Minimal APIs
- **Rôle** : exposer les endpoints HTTP (pas de contrôleurs MVC).
- **Pourquoi** : endpoints déclaratifs légers, parfaits pour le pattern « endpoint = traducteur HTTP→MediatR ».
- **Config** : `app.MapGroup("/seller/products")…MapPost(...)` dans les fichiers `Endpoints/*.cs` ;
  `DisableAntiforgery()` sur les endpoints multipart.

---

## Communication interne

### MediatR 12.4.1
- **Rôle** : bus **in-process** CQRS (commandes/requêtes → handlers) + pipeline (behaviors).
- **Pourquoi** : découple endpoint et logique métier, permet des behaviors transverses (validation,
  logging) sans polluer les handlers. 12.x est la dernière ligne sous licence MIT.
- **Config** : `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly))` pour chaque
  `ApplicationAssembly` de module ; behaviors via `AddBuildingBlocksPipeline()`.

### FluentValidation 11.9.2
- **Rôle** : validation des commandes/requêtes.
- **Pourquoi** : règles déclaratives testables, exécutées **avant** le handler par `ValidationBehavior`.
- **Config** : `AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true)` dans chaque
  installer ; les échecs deviennent un `Result.Failure(Error.Validation(...))` → HTTP 400.

---

## Persistance

### Entity Framework Core 8 + Npgsql 8 (PostgreSQL)
- **Rôle** : ORM + provider Postgres. Une **base**, un **schéma par module**.
- **Pourquoi** : Postgres pour la robustesse relationnelle et le type `jsonb` (outbox) ; le schéma par
  module matérialise l'isolation sans multiplier les bases.
- **Config** :
  - Chaîne de connexion : `ConnectionStrings:Marketplace` (surchargée en conteneur par
    `ConnectionStrings__Marketplace`).
  - Par module : `AddDbContext<XDbContext>(o => o.UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", "<schema>")))`.
  - `HasDefaultSchema("<schema>")` dans `ModuleDbContext`.
  - Migrations exécutées au démarrage si `RunMigrationsAtStartup=true` (voir Program.cs de l'Api).
  - Outbox : colonne `Content` en `jsonb`, index sur `ProcessedOnUtc`.

---

## Cache & temps réel

### Redis (StackExchange) 8
- **Rôle** : cache distribué (paniers, read-models chauds) **et** backplane SignalR.
- **Pourquoi** : partager cache/sessions entre instances et propager les messages SignalR entre les
  différents hôtes (BFF Mobile/Web/Seller).
- **Config** : `Redis:ConnectionString` (sinon repli sur cache mémoire) ; `Redis:InstanceName` (préfixe de
  clés, défaut `marketplace:`) ; en conteneur `Redis__ConnectionString: "redis:6379,password=…"`.

### SignalR + backplane Redis 8
- **Rôle** : messagerie instantanée (acheteur ↔ vendeur) en temps réel.
- **Pourquoi** : push serveur→client ; le backplane Redis synchronise les hubs entre hôtes.
- **Config** : hubs mappés par les BFF concernés (ex. `/seller/hubs/chat`, `/mobile/hubs/chat`) ;
  jeton JWT accepté via query-string (`?access_token=`) pour la poignée de main WebSocket.

---

## Sécurité & identité

### JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer` 8)
- **Rôle** : authentification stateless par jeton.
- **Pourquoi** : un seul jeton accepté par les 5 hôtes (SSO interne).
- **Config** : section `Jwt` (`Issuer`, `Audience`, `SigningKey`). **La même `Jwt:SigningKey` DOIT être
  injectée dans les 5 hôtes** (`Jwt__SigningKey`). `ValidateIssuer/Audience/Lifetime/SigningKey = true`,
  `ClockSkew = 30 s`.

### BCrypt.Net-Next 4.0.3
- **Rôle** : hachage des mots de passe.
- **Pourquoi** : algorithme adaptatif salé, standard de fait.
- **Config** : via l'abstraction `IPasswordHasher` du module Identity.

### Otp.NET 1.4.0 (+ `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`)
- **Rôle** : MFA TOTP (codes à usage unique) ; émission/validation des JWT.
- **Pourquoi** : second facteur standard (compatible Google Authenticator).
- **Config** : endpoints `/api/identity/account/me/mfa/{setup,confirm,disable}`.

---

## API & documentation

### Swashbuckle (Swagger / OpenAPI) 6.6.2
- **Rôle** : documentation interactive + bouton « Authorize » (Bearer).
- **Pourquoi** : tester l'API sans client.
- **Config** : `AddSwaggerGen` avec sécurité `Bearer` ; schémas qualifiés par nom complet pour éviter les
  collisions entre modules. Actuellement activé sans condition (**à restreindre avant une mise en prod
  publique**).

### `Microsoft.Extensions.Http` (HttpClientFactory) 8
- **Rôle** : clients HTTP typés/nommés vers les services externes.
- **Pourquoi** : gestion du cycle de vie des `HttpClient`, résilience, réutilisation.
- **Config** : clients nommés (`cloudinary-image-processor`, `cloudflare-r2-media`, clients PSP FedaPay…)
  enregistrés dans les installers.

---

## Services externes intégrés

### Cloudflare R2 (stockage média, API compatible S3)
- **Rôle** : stockage **public** des images produit et **privé** des pièces KYB.
- **Pourquoi** : coût/egress avantageux, compatible S3 (signature AWS V4 sans SDK).
- **Config** : `Storage:CloudflareR2Media:*` (images, bucket public) et `Storage:CloudflareR2:*` (KYB,
  bucket privé) — `AccountId`, `AccessKeyId`, `SecretAccessKey`, `BucketName`, `PublicBaseUrl`, `KeyPrefix`.
  Secrets via `.env`/vault (`R2_*`). Tant que non configuré → stub/simulé.

### Cloudinary (traitement d'image)
- **Rôle** : détourage IA (`e_background_removal`) + fond blanc (`b_white`) des photos produit.
- **Pourquoi** : add-on IA prêt à l'emploi ; on ne garde Cloudinary que pour **traiter**, l'image finale
  est rapatriée dans R2 puis l'asset Cloudinary est détruit.
- **Config** : `Media:Cloudinary:{CloudName,ApiKey,ApiSecret,MaxWaitSeconds}` (env `CLOUDINARY_*`). Auth
  d'upload passée en **query-string** (l'API lit `api_key/timestamp/signature`) ; signature SHA1 hex.
  Non configuré → `NullImageProcessor` (image inchangée). Détails : [07-seller-flow.md](07-seller-flow.md#images).

### FedaPay (Mobile Money — MTN, Moov, Celtis)
- **Rôle** : paiements et **versements (payouts)** vendeurs en Afrique de l'Ouest (XOF).
- **Pourquoi** : agrégateur Mobile Money local. Le `mode` de payout est dérivé de l'opérateur du vendeur
  (`mtn_open` / `moov` / `sbin`).
- **Config** : `Payments:FedaPay:{ApiKey,WebhookSecret,EnablePayouts}` (env `FEDAPAY_*`). `EnablePayouts=false`
  ⇒ payout simulé (stub) ; `true` ⇒ versements réels (nécessite l'activation côté compte FedaPay).

---

## Tests & garde-fous

### xUnit 2.9 + FluentAssertions 6.12 + Testcontainers.PostgreSQL 3.9
- **Rôle** : tests unitaires (domaine/application) et d'intégration (Postgres réel éphémère).
- **Pourquoi** : tester les règles métier sans mocker la base ; Testcontainers lance un Postgres jetable.
- **Config** : projets sous `tests/` (un par module + intégration).

### NetArchTest.Rules 1.3.2
- **Rôle** : **tests d'architecture** — vérifient que les frontières de modules/couches ne sont pas violées.
- **Pourquoi** : empêcher un module de référencer l'Infrastructure d'un autre, ou le Domain de dépendre de
  l'Application. C'est le garde-fou automatisé du modular monolith.
- **Config** : `tests/Architecture.Tests`.

---

## Infrastructure de déploiement

### Docker + Traefik v3 + Docker Compose
- **Rôle** : conteneurisation des 5 hôtes ; Traefik = reverse-proxy/edge (routage par hôte/préfixe, TLS
  Let's Encrypt, compression, en-têtes de sécurité, rate-limiting).
- **Config** : `deploy/Dockerfile` (multi-projets via `--build-arg PROJECT`), `deploy/docker-compose.*.yml`,
  `deploy/traefik/`.

### Ansible
- **Rôle** : déploiement idempotent sur VPS (installe Docker, synchronise le code, génère `.env.<env>`
  depuis un vault chiffré, `docker compose up`).
- **Config** : `deploy/ansible/` (inventories dev/staging/prod, `roles/`, `env.j2`, `vault.yml`). Voir
  [08-deployment-and-config.md](08-deployment-and-config.md) et `deploy/README.md`.

### GHCR (registry d'images)
- **Rôle** : héberge les images `ghcr.io/<user>/{api,bff-*}:<tag>`. Le VPS **pull** (ne compile pas).
- **Config** : `deploy/build-and-push.sh` (buildx `linux/amd64`), `registry_token` dans le vault.
