# Déploiement — Traefik + hôte unique

Traefik est le **point d'entrée unique** (edge). Il route le trafic vers `Marketplace.Host`,
seul service applicatif, adossé à Postgres et Redis. L'hôte compose les 25 modules
in-process et sert trois surfaces par chemin ; Traefik ne fait qu'aiguiller, terminer la
compression et appliquer en-têtes de sécurité + limitation de débit.

```
                       ┌──────────── Traefik (:80) ────────────┐
   navigateur / app ──▶│  api.localhost     /api               │──▶ Marketplace.Host ─┬─▶ Postgres
                       │                                       │    /api/client       │   Redis
                       │                                       │    /api/sellers      │
                       │                                       │    /api/admin        ┘
                       └───────────────────────────────────────┘
                          dashboard : http://traefik.localhost (ou :8080)
```

## Démarrer

```bash
cd deploy
cp .env.example .env          # puis renseigne JWT_SIGNING_KEY et PLATFORM_SECRET_KEY
docker compose up --build
```

**Postgres ne démarre pas avec la pile.** Il tourne déjà dans un conteneur sur le
poste ; en lancer un second échoue sur le port 5434, ou — quand le port est libre —
ouvre une base VIDE à côté de celle qui porte les données, et les migrations
s'appliquent au mauvais endroit.

Il n'y a plus de service `postgres` ici : la base tourne dans son propre conteneur.
Pour que l'hôte la joigne, le plus propre est de **partager un réseau Docker** — pas
de republier le port sur l'hôte.

Dans le compose de la base, ajouter trois lignes :

```yaml
services:
  db:
    networks: [marketplace-net]      # en plus de ses réseaux existants

networks:
  marketplace-net:
    external: true
```

Puis dans `deploy/.env` :

```
MARKETPLACE_CONNECTION=Host=db;Port=5432;Database=marketplace;Username=…;Password=…
```

`Host=db` est le NOM DU SERVICE, résolu par le DNS interne de Docker. On ne passe
donc plus par l'hôte du tout : ni `localhost` (qui désigne le conteneur lui-même),
ni `host.docker.internal` (qui ne résout pas partout, et qu'un port publié sur
`127.0.0.1` refuserait de toute façon). La base reste inaccessible depuis le réseau.

Le réseau `marketplace-net` porte un nom fixe, indépendant du projet Compose :
c'est ce qui permet à un autre compose de le déclarer `external`. Lancer la pile
applicative au moins une fois le crée ; sinon, `docker network create marketplace-net`.

**MinIO ne démarre pas non plus.** Son image n'est plus tirable depuis Docker Hub,
et Compose abandonnait toute la pile pour un stockage objet dont l'API, Redis et
Traefik ne dépendent pas. `KYB_ENDPOINT=` vide fait retomber le stockage des pièces
KYB sur le stub hors-ligne. Pour l'exercer réellement :
`docker compose --profile with-minio up`, en pointant au besoin `MINIO_IMAGE` sur
`quay.io/minio/minio` — le registre que MinIO documente.

> Avant de toucher au stockage KYB, réactiver le profil : c'est le seul endroit où
> le chemin SigV4 et les URL présignées sont exercés hors production.

L'hôte applique les migrations de tous les modules au démarrage
(`RunMigrationsAtStartup=true`) — donc sur la base que désigne cette chaîne, et sur
aucune autre.

## Répéter la production, en local

```bash
docker compose -f docker-compose.yml -f docker-compose.prod-local.yml up --build
```

L'override ne change qu'`ASPNETCORE_ENVIRONMENT`. Base, Redis et Traefik restent
ceux du développement : le but n'est pas de reproduire le VPS, c'est de faire passer
à l'hôte les **gardes de démarrage de production** là où l'échec ne coûte rien —
clé JWT du dépôt, `Platform:SecretKey`, « aucun PSP configuré », « PSP réel sans
secret de webhook », cohérence clé FedaPay / URL de base.

Un démarrage refusé ici est un succès : c'est une panne de déploiement trouvée avant
le déploiement. Ne pas donner de valeur de complaisance aux variables manquantes dans
l'override — ce serait désactiver la répétition au moment où elle sert.

N'est PAS exercé : ACME, DNS, les étiquettes Traefik du compose de production, les
trois images des autres dépôts, et la base distante.

## Accès

Trois façons d'atteindre la même surface (les `*.localhost` pointent vers 127.0.0.1) :

| Entrée | URL |
|--------|-----|
| Par hôte, avec Swagger | http://api.localhost/swagger |
| Par préfixe | http://localhost/api/... |
| Depuis un téléphone sur le LAN | http://&lt;IP de la machine&gt;/api/... |

Les trois surfaces se distinguent par le chemin : `/api/client`, `/api/sellers`,
`/api/admin`. Les routes brutes des modules (`/api/catalog`, `/api/identity`…) existent
aussi en développement ; en production, Traefik ne publie que les trois surfaces et les
rappels des PSP.

Dashboard Traefik : http://traefik.localhost ou http://localhost:8080.

## Déployer le backend (staging / prod)

Le backend = **Marketplace.Host** + Postgres + Redis + Traefik, décrits dans
`docker-compose.staging.yml` / `docker-compose.prod.yml`. En production, seuls `api` et
`rembg` sortent de ce dépôt : Postgres vit sur un VPS dédié, Redis et Traefik sont ceux
de la machine.
Deux voies : **Ansible** (recommandé) ou **registry** (build local → pull serveur).

### A. Via Ansible (recommandé)

Prépare la machine de contrôle :

```bash
pip install ansible
cd deploy/ansible
ansible-galaxy collection install -r requirements.yml   # ansible.posix, community.docker
```

Renseigne les secrets de l'environnement (une fois) — inclut Postgres, JWT,
FedaPay, R2 **et Cloudinary** :

```bash
cd deploy/ansible/inventories/staging          # ou prod
# prod : group_vars/all/ (le groupe `database` lit le même vault)
# staging : group_vars/marketplace/
cp vault.example.yml group_vars/marketplace/vault.yml
#   → renseigner postgres/neon, jwt, platform_secret_key, fedapay, r2_*, cloudinary_* …
ansible-vault encrypt group_vars/marketplace/vault.yml
```

Déploie :

```bash
cd deploy/ansible

# 1er déploiement (installe Docker, build, up)
ansible-playbook -i inventories/staging/hosts.ini site.yml --ask-vault-pass

# Redéploiement rapide (build + up, sans réinstaller Docker)
ansible-playbook -i inventories/staging/hosts.ini site.yml --tags deploy --ask-vault-pass

# PROD
ansible-playbook -i inventories/prod/hosts.ini site.yml --tags deploy --ask-vault-pass
```

Le rôle `marketplace` synchronise le code, génère `deploy/.env.<env>` depuis le
vault (via `env.j2`), puis lance :

```bash
docker compose -f docker-compose.<env>.yml --env-file .env.<env> up -d --build --remove-orphans
```

### B. Via registry (build local amd64 → pull serveur)

Le VPS ne compile rien : on build+push l'image depuis un poste (utile depuis un
Mac arm64, le serveur étant amd64). Deux étiquettes sont poussées — l'étiquette
mobile (`staging`/`latest`) et `sha-<commit>`, immuable, qui est celle à déployer.

```bash
docker login ghcr.io                                   # ou docker.io
IMAGE_PREFIX=ghcr.io/mon-user IMAGE_TAG=staging ./deploy/build-and-push.sh
```

Puis, sur le serveur (`deploy/.env` renseigné, `IMAGE_PREFIX`/`IMAGE_TAG` cohérents) :

```bash
cd deploy
docker compose -f docker-compose.staging.yml --env-file .env pull
docker compose -f docker-compose.staging.yml --env-file .env up -d --force-recreate --remove-orphans
```

### Migrations

L'`api` applique les migrations EF **au démarrage** (`RunMigrationsAtStartup=true`
en staging/prod) : aucune commande manuelle. Vérifie les logs `api` au boot.

### Après un changement de code ou de secrets (ex. Cloudinary)

Renseigner `.env`/le vault ne suffit pas : il faut **reconstruire l'image** (nouveau
code) puis **recréer les conteneurs** (relire l'env).

```bash
# Ansible : le tag deploy fait rebuild + up
ansible-playbook -i inventories/staging/hosts.ini site.yml --tags deploy --ask-vault-pass

# …ou en direct sur le serveur, en ciblant les services concernés
cd deploy
docker compose -f docker-compose.staging.yml --env-file .env \
  up -d --build --force-recreate api
```

> Le traitement d'image (Cloudinary) tourne dans `api` : c'est ce service qu'il faut
> recréer après avoir renseigné les clés `CLOUDINARY_*`.

### Vérifier / diagnostiquer

```bash
docker compose -f docker-compose.staging.yml ps
docker compose -f docker-compose.staging.yml logs -f --tail=100 api
```

## Détourage des photos produit (rembg, local)

Le détourage IA + fond blanc tourne dans un conteneur `rembg` du réseau interne, avec
le modèle **u2net**. Il remplace Cloudinary : pas de quota, pas de facture à l'usage,
et les photos des vendeurs ne quittent pas le serveur.

**Rien à configurer dans le cas courant.** Le service est déclaré dans les compose
staging et prod, et l'hôte pointe dessus par défaut.

### Le commutateur d'arrêt

| Réglage dans le `.env` | Effet |
|---|---|
| `REMBG_URL` **commentée ou absente** | rembg actif (`http://rembg:7000`) |
| `REMBG_URL=` **présente et vide** | rembg désactivé → repli sur Cloudinary s'il est configuré, sinon image inchangée |

La distinction tient à la substitution `${REMBG_URL-…}` — **sans deux-points**, elle
sépare « absente » de « vide ». Écrire `${REMBG_URL:-…}` réunirait les deux cas et le
commutateur ne couperait plus rien. Côté Ansible, la ligne n'est rendue que si
`rembg_url` est défini dans l'inventaire, pour la même raison.

### Le choix de l'adaptateur est fait AU DÉMARRAGE

Ce n'est pas une chaîne de secours : si le conteneur rembg tombe, les détourages
échouent, ils ne basculent pas sur Cloudinary. Un basculement silencieux vers un
service payant, déclenché par une panne, est le genre de mécanisme qu'on découvre sur
une facture. Pour revenir à Cloudinary : vider `REMBG_URL`, redémarrer.

Une panne n'empêche pas de créer des produits — seule la photo n'est pas détourée, et
les consoles cessent de l'annoncer (`GET /seller/products/media/capabilities` reflète
la santé observée, avec deux minutes de latence après un échec).

### Licences — le point qui engage

`rembg` est sous MIT, **mais ses modèles ne le sont pas tous**. Ceux de la famille BRIA
(`bria-rmbg`) sont en CC BY-NC 4.0 : usage non commercial, donc interdits ici.

Le code applique une **liste blanche** (`u2net`, `u2netp`, `u2net_human_seg`,
`silueta`) ; toute autre valeur de `REMBG_MODEL` retombe sur `u2net`. Ajouter une
entrée suppose d'avoir lu la licence des **poids**, pas seulement celle de rembg.

### Dimensionnement

`REMBG_THREADS` (défaut 2) borne les détourages **simultanés** — c'est l'argument
`--threads` de rembg. Sans lui, le serveur en tenterait jusqu'à quarante de front et
le conteneur serait tué pour dépassement mémoire bien avant de saturer le processeur.

Sur un petit VPS : `REMBG_CPUS=1`, `REMBG_MEMORY=1500M`, `REMBG_THREADS=1`, et
abaisser le parallélisme d'envoi côté console vendeur (paquets de trois par défaut).

L'image pèse environ 1 Go et n'est publiée qu'en **linux/amd64** : sur un VPS ARM, elle
ne démarrera pas.

### Vérifier

```bash
# Le service répond (depuis le serveur)
docker compose -f docker-compose.staging.yml exec api \
  curl -fsS http://rembg:7000/api > /dev/null && echo OK

# Ce que la console vendeur croit savoir
#   → { "backgroundRemoval": true }
```

Le premier détourage après un démarrage à froid met la session ONNX en place : comptez
quelques secondes de plus que les suivants.

## Console vendeur (`pro.hba-express.com`)

Dépôt : `HbaExpress_seller_dashboard`. Elle se déploie comme la console admin — image
construite par GitHub Actions, poussée sur GHCR, tirée par la stack du VPS.

`develop_staging` → staging (`seller-console:staging`) · `develop` → prod (`seller-console:latest`)

### ⚠️ Ordre de la PREMIÈRE mise en service

Ansible lance `docker compose up -d --no-build` sur **toute** la pile. Si l'image
`seller-console` n'est pas encore sur GHCR, le déploiement échoue **et emporte les
autres services**. Dans cet ordre, donc :

1. **DNS** — créer l'enregistrement A / AAAA vers le VPS. Sans lui, Let's Encrypt ne
   peut pas émettre le certificat, et Traefik servira une erreur TLS.

   ⚠️ **La console vendeur n'est PAS sur `$ROOT_DOMAIN`.** En prod elle répond sur
   `pro.hba-express.com`, alors que toute l'infrastructure (api, m, web, seller, admin,
   console) vit sous `hba-express.org`. C'est pourquoi le compose lit une variable
   dédiée, `SELLER_CONSOLE_DOMAIN` (`seller_console_domain` côté Ansible), et non
   `pro.${ROOT_DOMAIN}` : ce dernier aurait produit `pro.hba-express.org`, un nom que
   personne n'utilise — mais pour lequel Let's Encrypt aurait quand même émis un
   certificat. Le déploiement serait passé au vert et `pro.hba-express.com` aurait
   répondu 404. Laissée vide, la variable retombe sur `pro.${ROOT_DOMAIN}` : c'est le
   comportement voulu en staging, où tout partage la même racine.

   Si la zone `hba-express.com` est gérée par **Cloudflare**, l'enregistrement doit
   être en **« DNS only » (nuage GRIS)**. Proxifié (nuage orange), Cloudflare termine
   le TLS lui-même et intercepte le challenge HTTP-01 : Traefik n'obtient jamais son
   certificat. Le domaine sert déjà le site vitrine sur Cloudflare Pages et les e-mails
   Resend — vérifier de ne pas toucher aux enregistrements existants (`@`, `www`, DKIM,
   SPF).
2. **Secrets GitHub** — dans `HbaExpress_seller_dashboard`, environnements `staging`
   et `prod` : `GHCR_TOKEN`, `DEPLOY_SSH_HOST`, `DEPLOY_SSH_USER`, `DEPLOY_SSH_KEY`
   (et `DEPLOY_SSH_PORT` si ce n'est pas 8022).
3. **Secret de session** — `seller_session_secret` dans le vault Ansible :
   `openssl rand -base64 32`. **Jamais celui de la console admin** : deux surfaces
   distinctes, deux compromissions indépendantes. Vide, le compose échoue sur un `:?`
   — c'est voulu.
4. **Publier l'image** — pousser sur `develop_staging`, attendre le workflow vert.
5. **Déployer le backend** — Ansible, comme d'habitude. La console démarre avec.

Une fois la première mise en service faite, l'ordre n'a plus d'importance : chaque
push sur le dépôt de la console redéploie ce seul service.

### Ce que le workflow vérifie

`tsc --noEmit`, puis `next build`, **avant** de construire l'image : une erreur de type
ne produit aucune image. Après le déploiement, il attend que le conteneur soit
`healthy` et **échoue** sinon, en affichant les 80 dernières lignes de journal — sans
cela, un démarrage raté (secret absent, backend injoignable) remonterait en vert.

Chaque image porte deux étiquettes : `:staging`/`:latest` (mobiles) et `:<sha>` (fixe).
La seconde permet de savoir quel commit tourne, et de revenir en arrière sans
reconstruire :

```bash
cd /opt/marketplace   # ou /opt/marketplace-staging
sudo docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  up -d --no-deps seller-console
# retour arrière : éditer IMAGE_TAG, ou lancer l'image :<sha> voulue
```

### Vérifier

```bash
# État et santé du conteneur
sudo docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod ps seller-console

# La console voit-elle l'hôte ? (depuis le réseau interne)
sudo docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec seller-console node -e "require('http').get('http://api:8080/health',r=>console.log(r.statusCode))"
```

## Notes

- **JWT** : `JWT_SIGNING_KEY` (dans `.env`) est injecté via `Jwt__SigningKey`. Les
  variables d'environnement priment sur les `appsettings.json`, donc tes secrets
  locaux ne sont pas utilisés ici.
- **Base de données** : la chaîne de connexion est surchargée vers le service
  `postgres` (port interne 5432). Le volume `pgdata` persiste les données.
- **Traefik** : `api.insecure: true` et le dashboard sur `:8080` sont réservés au
  développement. Avant un déploiement public : passer en HTTPS (entrypoint
  `websecure` + résolveur ACME), retirer l'accès insecure, et restreindre le CORS
  de la console admin (`Cors:Admin:Origins`).
- **Outbox** : l'hôte embarque les modules et leurs processeurs d'arrière-plan. Un
  seul processus traite donc l'outbox. Si l'hôte est répliqué un jour, dédier le
  traitement d'arrière-plan à une réplique (`OUTBOX_ENABLED=false` sur les autres)
  pour éviter la concurrence.
