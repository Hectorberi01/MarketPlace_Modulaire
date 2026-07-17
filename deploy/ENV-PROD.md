# Variables d'environnement — Production

Référence des variables attendues par le backend en **production** (le monolithe
`api` + les 4 BFF `mobile` / `web` / `seller` / `admin`).

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
🔐 secret → **vault** (`group_vars/marketplace/vault.yml`, chiffré).

Deux démarrages possibles côté conteneur :

1. `${VAR:?message}` — le compose **refuse de démarrer** si la variable manque.
2. `${VAR:-défaut}` — valeur de repli ; vide signifie généralement « fonctionnalité stubée/désactivée ».

---

## 1. Cœur — obligatoires (le service ne démarre pas sans)

| Variable | Oblig. | Secret | Clé .NET | Description |
|---|:---:|:---:|---|---|
| `MARKETPLACE_CONNECTION` | ✅ | 🔐 | `ConnectionStrings__Marketplace` | Chaîne **Npgsql** de la base (Neon/Postgres managé). Endpoint **DIRECT** (sans `-pooler`) pour les migrations EF. Ex. `Host=ep-xxxx.eu-west-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=…;SSL Mode=Require;Channel Binding=Require`. `:?` → bloque le démarrage si absente. |
| `REDIS_PASSWORD` | ✅ | 🔐 | (compose) `Redis__ConnectionString=redis:6379,password=…` | Mot de passe Redis (backplane SignalR + cache distribué). Fort et aléatoire. Sert aussi à lancer le conteneur `redis`. `:?`. |
| `JWT_SIGNING_KEY` | ✅ | 🔐 | `Jwt__SigningKey` | Clé de signature des JWT, **identique** sur l'API et les 4 BFF, ≥ 32 octets. `openssl rand -base64 48`. `:?`. |
| `ROOT_DOMAIN` | ✅ | — | (labels Traefik) | Domaine racine. Les surfaces sont des sous-domaines : `api.` `m.` `web.` `seller.` `admin.` doivent pointer (DNS A/AAAA) vers l'IP du serveur. Sert au routage, au CORS admin par défaut et aux liens e-mail. |
| `ACME_EMAIL` | ✅ | — | (Traefik) | E-mail Let's Encrypt (émission/renouvellement TLS). `:?`. |

> Note : `MARKETPLACE_CONNECTION`, `REDIS_PASSWORD`, `JWT_SIGNING_KEY` sont
> secrets → **vault**. `ROOT_DOMAIN`/`ACME_EMAIL` sont non sensibles (group_vars).

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
| `ADMIN_CORS_ORIGINS` | ⬜ | — | `Cors__Origins` (bff-admin) | Origines navigateur autorisées de la console admin. Défaut : `https://admin.${ROOT_DOMAIN}`. **Jamais `localhost` en prod.** |
| `SELLER_CORS_ORIGINS` | ⬜ | — | (non câblé en prod) | Présent dans le `.env`, mais **non injecté** par `docker-compose.prod.yml` (le tableau de bord vendeur Blazor est décommissionné). Sans effet en prod. |
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

| Clé .NET | api | bff-mobile | bff-web | bff-seller | bff-admin |
|---|:---:|:---:|:---:|:---:|:---:|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` | `Production` | `Production` | `Production` |
| `Jwt__Issuer` / `Jwt__Audience` | `marketplace` | idem | idem | idem | idem |
| `RunMigrationsAtStartup` | **`true`** | `false` | `false` | `false` | `false` |
| `OUTBOX_ENABLED` | *(non posé → actif)* | `false` | `false` | `false` | `false` |
| `Cors__Origins` | — | — | — | — | `ADMIN_CORS_ORIGINS` |
| `Notifications__Fcm__ServiceAccountPath` | fichier monté | idem | idem | idem | idem |

Points saillants :

- **Seul `api` applique les migrations** au démarrage (`RunMigrationsAtStartup=true`) ;
  les BFF attendent qu'il soit *healthy* (`depends_on`).
- **Seul `api` draine l'outbox** ; les BFF ont `OUTBOX_ENABLED=false` (mais peuvent
  lire/rejouer les *dead-letters* via la console admin).
- L'**API n'est pas exposée entièrement** : Traefik ne route que
  `/api/payments/webhooks` et `/api/payments/return` vers `api.${ROOT_DOMAIN}` ; le
  reste répond 404 au bord (défense en profondeur). Un nouveau webhook PSP doit être
  ajouté explicitement dans les labels Traefik.

---

## Secrets à mettre dans le vault

`group_vars/marketplace/vault.yml` (chiffré `ansible-vault encrypt`) :

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
- [ ] `ROOT_DOMAIN` réel (≠ `example.com`) + DNS des 5 sous-domaines → IP serveur.
- [ ] Base : `POSTGRES_PASSWORD` (co-hébergé) **ou** `marketplace_connection` (externe) ; `REDIS_PASSWORD`, `JWT_SIGNING_KEY`, `ACME_EMAIL`.
- [ ] Sauvegardes DB planifiées (`deploy/postgres/backup.sh` en cron) + bucket R2 `backups` privé.
- [ ] `ADMIN_EMAIL` + `ADMIN_PASSWORD` — pour que le **premier admin** soit créé au 1er boot de l'API.
- [ ] R2 renseigné si tu veux du vrai stockage images/KYB.
- [ ] Vault **chiffré** ; rien de secret en clair dans `main.yml` ou un `.env` versionné.
