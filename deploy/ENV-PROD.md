# Variables d'environnement — Production

Référence des variables attendues par le backend en **production** : l'hôte unique
`api` (`Marketplace.Host`), qui sert `/api/client`, `/api/sellers` et `/api/admin`.

Sources de vérité : [`.env.prod.example`](.env.prod.example),
[`docker-compose.prod.yml`](docker-compose.prod.yml) et le template Ansible
`ansible/roles/marketplace/templates/env.j2`.

---

## Comment ça s'articule

- Tu renseignes des variables **à noms courts** (`MARKETPLACE_CONNECTION`,
  `JWT_SIGNING_KEY`, …) dans un fichier `.env.prod` (gitignoré), ou via les
  `group_vars` + le **vault Ansible** pour les secrets.
- `docker-compose.prod.yml` **traduit** ces noms courts vers les clés de config
  hiérarchiques .NET (convention double underscore `__`, ex.
  `ConnectionStrings__Marketplace`) injectées dans chaque conteneur.
- Certaines clés .NET sont **fixées** par le compose (pas dans le `.env`) :
  `ASPNETCORE_ENVIRONMENT`, `Jwt__Issuer/Audience`, `RunMigrationsAtStartup`,
  `OUTBOX_ENABLED`, `Cors__Origins`, `Notifications__Fcm__ServiceAccountPath`.
  Voir la section [Variables fixées par le compose](#variables-fixées-par-le-compose).

**Légende :** ✅ obligatoire · ⬜ optionnel (dégradation propre si vide) ·
🔐 secret → **vault** (prod : `group_vars/all/vault.yml`, chiffré).

Deux démarrages possibles côté conteneur :

1. `${VAR:?message}` — le compose **refuse de démarrer** si la variable manque.
2. `${VAR:-défaut}` — valeur de repli ; vide signifie généralement « fonctionnalité stubée/désactivée ».

---

## 1. Cœur — obligatoires (le service ne démarre pas sans)

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `MARKETPLACE_CONNECTION` | ✅ | 🔐 | `ConnectionStrings__Marketplace` | Chaîne **Npgsql** de la base (Neon/Postgres managé). Endpoint **DIRECT** (sans `-pooler`) pour les migrations EF. Ex. `Host=ep-xxxx.eu-west-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=…;SSL Mode=Require;Channel Binding=Require`. `:?` → bloque le démarrage si absente. |
| `REDIS_CONNECTION` | ✅ | — | `Redis__ConnectionString` | Assemblée par Ansible depuis `redis_host`, `redis_port` et `redis_password`. En production, Redis est **celui du VPS** : ce dépôt n'en déploie plus. Renseigner `redis_host` et `redis_network` (group_vars) — vides, le déploiement est refusé. |
| `REDIS_INSTANCE_NAME` | ⬜ | — | `Redis__InstanceName` | Préfixe des clés, `marketplace:` par défaut. Décisif sur un Redis **partagé** avec d'autres applications du VPS. |
| `REDIS_PASSWORD` | ⬜ | 🔐 | (entre dans `REDIS_CONNECTION`) | Mot de passe du Redis de l'hôte. Clé absente du vault = chaîne sans `password=`. En staging, sert aussi à lancer son conteneur `redis`. |
| `JWT_SIGNING_KEY` | ✅ | 🔐 | `Jwt__SigningKey` | Clé de signature des JWT, ≥ 32 octets. `openssl rand -base64 48`. `:?`. |
| `PLATFORM_SECRET_KEY` | ✅ | 🔐 | `Platform__SecretKey` | Chiffre (AES-256-GCM) les secrets des fournisseurs — PSP, Resend, Twilio, Meta — que la console admin enregistre en base. **32 octets exactement** : `openssl rand -base64 32`. `:?`. L'hôte refuse de démarrer sans elle, et refuse la clé de développement. |
| `ROOT_DOMAIN` | ✅ | — | (labels Traefik) | Domaine racine. Depuis la fusion des BFF, UN seul hôte sert les trois surfaces par chemin : seul `api.` doit pointer (DNS A/AAAA) vers l'IP du serveur. Les consoles et la vitrine ont leurs propres noms complets (`seller_console_domain`, `site_domain`, `admin_console_domain`). Sert au routage, au CORS admin par défaut et aux liens e-mail. |
| `ACME_EMAIL` | ⬜ | — | (Traefik de staging) | E-mail Let's Encrypt. **Non utilisé en production** : le proxy du VPS obtient les certificats avec SON compte ACME, et ce dépôt n'y installe aucun Traefik. |

> Note : `MARKETPLACE_CONNECTION`, `REDIS_PASSWORD`, `JWT_SIGNING_KEY` et
> `PLATFORM_SECRET_KEY` sont secrets → **vault**. `ROOT_DOMAIN` est non sensible
> (group_vars).

---

## 1b. Base de données — deux options

**Option A — Postgres CO-HÉBERGÉ sur le VPS** (service `postgres` du compose).
C'est le cas par défaut. Tu ne définis que le mot de passe : côté Ansible,
`env.j2` **fabrique** `MARKETPLACE_CONNECTION` tout seul (host = service `postgres`,
sans SSL sur le réseau Docker interne).

| Variable | Oblig. | Secret | Clé / usage | Description |
|---|:---:|:---:|---|---|
| `POSTGRES_PASSWORD` | ✅ | 🔐 | conteneur `postgres` **+** chaîne de connexion | Mot de passe du superutilisateur Postgres. `:?` (le conteneur ne démarre pas sans). Vault : `postgres_password`. |
| `POSTGRES_DB` | ⬜ | — | conteneur `postgres` | Nom de la base. Défaut `marketplace`. |
| `POSTGRES_USER` | ⬜ | — | conteneur `postgres` | Rôle. Défaut `postgres`. |
| `npgsql_max_pool_size` | ⬜ | — | (Ansible) `Maximum Pool Size` | Plafond du pool **par hôte** (5 hôtes). Défaut `15` → 75 connexions max. |

> La base n'expose **aucun port** vers Internet (pas de `ports:`), uniquement le
> réseau Docker interne. Volume persistant `pgdata`. Réglages dans
> `deploy/postgres/postgresql.conf` (tuné 24 Go). **Sauvegardes** :
> `deploy/postgres/backup.sh` (`pg_dump` → R2), à planifier en cron.

**Option B — base EXTERNE managée (Neon/Supabase).** Renseigne
`marketplace_connection` (vault) au format Npgsql, endpoint **direct** (sans
`-pooler`, requis pour les migrations EF). Elle est **prioritaire** ; dans ce cas,
retire le service `postgres` du compose et laisse `POSTGRES_*` vides.

---

## 2. E-mails transactionnels (Resend) — obligatoire en production

L'API **refuse de démarrer en Production** sans ces trois-là : sans canal e-mail,
personne ne peut vérifier son adresse ni récupérer un mot de passe oublié.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `RESEND_API_KEY` | ✅ | 🔐 | `Notifications__Email__ApiKey` | Clé API Resend. |
| `EMAIL_FROM` | ✅ | — | `Notifications__Email__From` | Adresse expéditrice. Le **domaine doit être vérifié** chez Resend (SPF + DKIM), sinon 403 à chaque envoi. |
| `EMAIL_APP_BASE_URL` | ✅ | — | `Notifications__Email__AppBaseUrl` | Base des liens cliquables des e-mails, ex. `https://${ROOT_DOMAIN}`. |

---

## 2b. Amorçage du premier administrateur (bootstrap)

Sur une base neuve, **aucun compte n'existe** et créer un admin exige déjà d'être
admin. Ces variables cassent la boucle : au **1er démarrage de l'API** (celui qui
migre/seed), un compte admin est créé s'il n'existe pas. **Idempotent** — un
redéploiement ne recrée rien et **ne réinitialise jamais** le mot de passe. Injecté
**uniquement sur le service `api`**.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `ADMIN_EMAIL` | ⬜* | — | `Identity__Bootstrap__AdminEmail` | E-mail de l'admin à créer. *Requis (avec `ADMIN_PASSWORD`) pour l'amorçage.* |
| `ADMIN_PASSWORD` | ⬜* | 🔐 | `Identity__Bootstrap__AdminPassword` | Mot de passe initial. Le changer après la 1re connexion, puis on peut le retirer. |
| `ADMIN_FIRST_NAME` | ⬜ | — | `Identity__Bootstrap__AdminFirstName` | Prénom. Défaut `Admin`. |
| `ADMIN_LAST_NAME` | ⬜ | — | `Identity__Bootstrap__AdminLastName` | Nom. Défaut `Plateforme`. |
| `ADMIN_PHONE` | ⬜ | — | `Identity__Bootstrap__AdminPhone` | Téléphone (`^\+?[0-9]{8,15}$`). Défaut `+22900000000`. |

> Les deux ensemble (`ADMIN_EMAIL` **et** `ADMIN_PASSWORD`) déclenchent l'amorçage ;
> l'un vide = pas d'amorçage. Le compte est **actif** (rôle `Admin`) mais son e-mail
> n'est **pas** marqué vérifié (aucun message n'a été envoyé). Vault : `admin_password` ;
> `admin_email` + prénom/nom/téléphone → `group_vars/marketplace/main.yml`.

---

## 3. Paiements — FedaPay (requis pour encaisser réellement)

Vides = valeurs `appsettings.json` (⚠️ **bac à sable**) : la prod parlerait au sandbox,
aucun encaissement réel.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `FEDAPAY_BASE_URL` | ⬜ | — | `Payments__FedaPay__BaseUrl` | `https://api.fedapay.com/v1` (live) ou `https://sandbox-api.fedapay.com/v1` (défaut). L'URL et la clé doivent viser le **même monde**, sinon refus de démarrage. |
| `FEDAPAY_API_KEY` | ⬜* | 🔐 | `Payments__FedaPay__ApiKey` | Clé `sk_live_…` en prod. *Requis pour les paiements réels.* |
| `FEDAPAY_WEBHOOK_SECRET` | ⬜* | 🔐 | `Payments__FedaPay__WebhookSecret` | Secret de signature des webhooks. **Sans lui, aucun paiement n'est confirmé.** *Requis pour les paiements réels.* |
| `FEDAPAY_ENABLE_PAYOUTS` | ⬜ | — | `Payments__FedaPay__EnablePayouts` | `true` = versements vendeur réels (impossible en sandbox : 403). `false` (défaut) = versements simulés. |

---

## 4. Stockage objet — Cloudflare R2 (requis pour images & KYB réels)

Vides = stockage **simulé** (stub hors-ligne). Les identifiants S3 sont **partagés**
entre les deux buckets.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `R2_ACCOUNT_ID` | ⬜* | 🔐 | `Storage__CloudflareR2__AccountId` **et** `Storage__CloudflareR2Media__AccountId` | Compte Cloudflare. |
| `R2_ACCESS_KEY_ID` | ⬜* | 🔐 | `…__AccessKeyId` (les deux) | Clé d'accès S3. |
| `R2_SECRET_ACCESS_KEY` | ⬜* | 🔐 | `…__SecretAccessKey` (les deux) | Secret d'accès S3. |
| `R2_BUCKET_NAME` | ⬜* | — | `Storage__CloudflareR2__BucketName` | Bucket **privé** des pièces KYB. Ex. `documents-pdf`. **Ne jamais rendre public.** |
| `R2_PUBLIC_BASE_URL` | ⬜ | — | `Storage__CloudflareR2__PublicBaseUrl` | Laisser **vide** pour le KYB (accès par URL présignée). |
| `R2_MEDIA_BUCKET_NAME` | ⬜* | — | `Storage__CloudflareR2Media__BucketName` | Bucket **public** des images produit. Ex. `images`. |
| `R2_MEDIA_KEY_PREFIX` | ⬜ | — | `Storage__CloudflareR2Media__KeyPrefix` | Préfixe des clés, ex. `MarketPlace`. |
| `R2_MEDIA_PUBLIC_BASE_URL` | ⬜* | — | `Storage__CloudflareR2Media__PublicBaseUrl` | URL publique des images. En prod, préférer un **domaine personnalisé** connecté au bucket (pas la limite `r2.dev`). |

### 4.1 Un bucket par usage de média

Le bucket ci-dessus reste la destination PAR DÉFAUT. Chaque usage peut recevoir le sien.

**Deux variables, deux effets distincts, à ne pas renseigner en même temps :**

| Ce qu'on renseigne | Ce qui se passe |
|---|---|
| le NOM seul | le bucket est **créé** au démarrage s'il n'existe pas ; aucun trafic n'y va encore |
| le nom **et** l'URL | les téléversements de cet usage partent vers ce bucket |

La séquence est donc : nommer → déployer → rattacher un domaine au bucket dans le
tableau de bord Cloudflare → renseigner l'URL → redéployer. Entre les deux, l'usage
retombe sur le bucket par défaut et **les images continuent de s'afficher**.

Un bucket créé par l'API S3 est **privé** : lui rattacher un domaine public
(`r2.dev` ou personnalisé) n'existe pas dans l'API S3 et reste une étape manuelle.
C'est toute la raison de ce fonctionnement en deux temps.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `R2_MEDIA_AUTO_CREATE_BUCKETS` | ⬜ | — | `Storage__CloudflareR2Media__AutoCreateBuckets` | `true` par défaut. Créer un bucket exige un jeton **Admin Read & Write** : avec un jeton « Object Read & Write », la tentative renvoie 403, l'échec est journalisé et l'API démarre normalement. Le mettre à `false` et créer les buckets à la main est le choix le plus strict. |
| `R2_MEDIA_BUCKET_CATEGORIES` / `R2_MEDIA_URL_CATEGORIES` | ⬜ | — | `…__Buckets__CategoryImage__…` | Visuels de rayon, famille et catégorie. |
| `R2_MEDIA_BUCKET_BANNERS` / `R2_MEDIA_URL_BANNERS` | ⬜ | — | `…__Buckets__ContentBanner__…` | Bannières de l'accueil et des pages éditoriales. |
| `R2_MEDIA_BUCKET_SHOPS` / `R2_MEDIA_URL_SHOPS` | ⬜ | — | `…__Buckets__ShopLogo__…` et `…__Buckets__ShopBanner__…` | Logo **et** couverture des boutiques vendeur : même propriétaire, même visibilité, un seul bucket. Les racines de clé `boutiques-logo/` et `boutiques-couverture/` les séparent. |
| `R2_MEDIA_BUCKET_AVATARS` / `R2_MEDIA_URL_AVATARS` | ⬜ | — | `…__Buckets__Avatar__…` | Photos de profil. Aucun écran ne les téléverse encore. |
| `R2_MEDIA_BUCKET_CHAT` / `R2_MEDIA_URL_CHAT` | ⬜ | — | `…__Buckets__ChatAttachment__…` | Pièces jointes de conversation et de litige. **Ces contenus ne sont pas destinés au public.** Le bucket est séparé précisément pour qu'on puisse lui retirer tout domaine public et basculer la lecture en URL présignée sans retoucher un appelant. Tant qu'un domaine y est rattaché, l'URL reste lisible par quiconque la possède. |

Racines de clé, identiques dans tous les buckets et **stables** — c'est le premier
segment qui permet à la suppression de retrouver le bucket d'un objet :
`products/` (historique), `categories/`, `bannieres/`, `boutiques-logo/`,
`boutiques-couverture/`, `avatars/`, `messagerie/`.

---

## 5. Traitement d'image — Cloudinary (optionnel)

Vide = pas de détourage (image inchangée). Add-on « AI Background Removal » requis.

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `CLOUDINARY_CLOUD_NAME` | ⬜ | 🔐 | `…Cloudinary__CloudName` | Nom d'environnement Cloudinary. |
| `CLOUDINARY_API_KEY` | ⬜ | 🔐 | `…Cloudinary__ApiKey` | Clé API. |
| `CLOUDINARY_API_SECRET` | ⬜ | 🔐 | `…Cloudinary__ApiSecret` | Secret API. |

---

## 6. Push — Firebase Cloud Messaging (optionnel)

Ce **n'est pas** une variable d'env classique : le JSON du compte de service est
stocké dans le **vault** (`fcm_service_account_json`), écrit par Ansible dans
`deploy/fcm/service-account.json`, puis monté en lecture seule. Fichier absent →
`NullPushSender` (aucun push, pas d'erreur).

| Élément | Oblig. | Secret | Détail |
|---|:---:|:---:|---|
| `fcm_service_account_json` (vault) | ⬜ | 🔐 | JSON intégral du compte de service Firebase. Rendu vers le fichier ci-dessous. |
| `Notifications__Fcm__ServiceAccountPath` | — | — | **Fixé** par le compose à `/run/secrets/fcm/service-account.json` (volume `./fcm:/run/secrets/fcm:ro`). |

---

## 7. CORS & comportement des comptes (optionnels)

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `CLIENT_CORS_ORIGINS` | ⬜ | — | `Cors__Client__Origins` | Origines navigateur autorisées sur `/api/client` (vitrine, hub de messagerie). Vide = aucune origine acceptée. |
| `SELLER_CORS_ORIGINS` | ⬜ | — | `Cors__Seller__Origins` | Idem sur `/api/sellers`. Défaut Ansible : `https://${SELLER_CONSOLE_DOMAIN}`. |
| `ADMIN_CORS_ORIGINS` | ⬜ | — | `Cors__Admin__Origins` | Idem sur `/api/admin`. Défaut Ansible : `https://${ADMIN_CONSOLE_DOMAIN}`. **Jamais `localhost` en prod.** |

> **Trois listes, jamais une seule.** Les trois surfaces partagent l'origine serveur de
> l'hôte : une liste commune autoriserait la vitrine à appeler `/api/admin` depuis le
> navigateur d'un administrateur connecté.
| `REQUIRE_APPROVAL_BUYERS` | ⬜ | — | `Identity__Registration__RequireApprovalForBuyers` | `true` (défaut) : toute inscription publique attend l'aval d'un admin. `false` : acheteurs actifs immédiatement. |
| `REQUIRE_APPROVAL_ADMIN_CREATED` | ⬜ | — | `Identity__Registration__RequireApprovalForAdminCreated` | `false` (défaut) : comptes créés depuis la console admin actifs d'emblée. |

---

## 8. Registry d'images (optionnel — si `use_registry=true`)

| Variable | Oblig. | Secret | Description |
|---|:---:|:---:|---|
| `IMAGE_PREFIX` | ⬜ | — | Préfixe des images, ex. `ghcr.io/org/marketplace`. Défaut `marketplace` (build local). |
| `IMAGE_TAG` | ⬜ | — | Tag d'image, ex. `latest` / `prod`. |

> Sans registry, les images sont **construites sur l'hôte** (`compose_build=true`,
> build séquentiel pour limiter la RAM).

---

## Variables fixées par le compose

Non à renseigner — posées automatiquement par `docker-compose.prod.yml`, avec des
**valeurs par service** :

| Clé .NET | api |
|---|:---:|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Jwt__Issuer` / `Jwt__Audience` | `marketplace` |
| `RunMigrationsAtStartup` | **`true`** |
| `OUTBOX_ENABLED` | *(non posé → actif)* |
| `Cors__{Client,Seller,Admin}__Origins` | les trois variables de la section 7 |
| `Monitoring__Surfaces__0__Url` | `http://api:8080` |
| `Notifications__Fcm__ServiceAccountPath` | fichier monté |

Points saillants :

- L'hôte applique les migrations au démarrage et draine l'outbox : un seul processus,
  donc aucune concurrence sur l'outbox.
- **La surface publique est réduite aux chemins qui ont un appelant externe** :
  Traefik ne route que `/api/client`, `/api/sellers`, `/api/admin`,
  `/api/payments/webhooks` et `/api/payments/return` ; le reste (`/api/catalog`,
  `/api/identity`, `/api/shops`…) répond 404 au bord. Un nouveau webhook PSP doit être
  ajouté explicitement dans les labels Traefik.
- **Noms d'hôte** : `api.${ROOT_DOMAIN}` est canonique ; `m.`, `seller.` et `admin.`
  restent acceptés par le même routeur tant que des builds mobiles les visent
  (`API_BASE_URL` est figé au build).

---

## Secrets à mettre dans le vault

`group_vars/all/vault.yml` en prod, `group_vars/marketplace/vault.yml` en staging
(chiffré par `ansible-vault encrypt`) :

`marketplace_connection`, `jwt_signing_key`, `redis_password`, `fedapay_api_key`,
`fedapay_webhook_secret`, `r2_account_id`, `r2_access_key_id`,
`r2_secret_access_key`, `cloudinary_cloud_name`, `cloudinary_api_key`,
`cloudinary_api_secret`, `resend_api_key`, `fcm_service_account_json`
(+ identifiants de registry si `use_registry=true`).

Non sensibles → `group_vars/marketplace/main.yml` : `root_domain`, `acme_email`,
`email_from`, `email_app_base_url`, `fedapay_base_url`, `fedapay_enable_payouts`,
les noms/URL de buckets R2, `admin_cors_origins`, les drapeaux `require_approval_*`.

---

## Checklist de bascule prod (bloqueurs connus)

Ces variables sont **vides par défaut** mais **critiques** — à renseigner avant d'ouvrir :

- [ ] `FEDAPAY_WEBHOOK_SECRET` — sans lui, **aucun paiement n'est confirmé**.
- [ ] `FEDAPAY_API_KEY` (`sk_live_…`) + `FEDAPAY_BASE_URL=https://api.fedapay.com/v1`.
- [ ] `RESEND_API_KEY` + `EMAIL_FROM` (domaine vérifié SPF/DKIM) + `EMAIL_APP_BASE_URL` — **sinon l'API ne démarre pas**.
- [ ] `ROOT_DOMAIN` réel (≠ `example.com`) + DNS de `api.` → IP serveur, plus les noms complets des consoles et de la vitrine.
- [ ] Base : `POSTGRES_PASSWORD` (co-hébergé) **ou** `marketplace_connection` (externe) ; `JWT_SIGNING_KEY`, `PLATFORM_SECRET_KEY`.
- [ ] Redis : `redis_host` + `redis_network` (group_vars) renseignés avec le conteneur et le réseau du VPS ; `redis_password` dans le vault si ce Redis en a un.
- [ ] Sauvegardes DB planifiées (rôle `backup` sur le VPS base) + bucket R2 `backups` privé.
- [ ] `ADMIN_EMAIL` + `ADMIN_PASSWORD` — pour que le **premier admin** soit créé au 1er boot de l'API.
- [ ] R2 renseigné si tu veux du vrai stockage images/KYB.
- [ ] Vault **chiffré** ; rien de secret en clair dans `main.yml` ou un `.env` versionné.
