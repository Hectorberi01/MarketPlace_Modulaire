# Déploiement — Traefik + monolithe + BFF

Traefik est le **point d'entrée unique** (edge). Il route le trafic vers les 4 BFF
et vers le monolithe `Marketplace.Api`, tous adossés à Postgres et Redis. Chaque
service est une composition in-process des 25 modules ; Traefik ne fait
qu'aiguiller, terminer la compression et appliquer en-têtes de sécurité +
limitation de débit.

```
                       ┌──────────── Traefik (:80) ────────────┐
   navigateur / app ──▶│  host-based            path-based     │
                       │  api.localhost         /api           │──▶ Marketplace.Api ─┐
                       │  mobile.localhost      /mobile         │──▶ Bff.Mobile       │
                       │  web.localhost         /web            │──▶ Bff.Web          ├─▶ Postgres
                       │  seller.localhost      /seller         │──▶ Bff.Seller       │   Redis
                       │  admin.localhost       /admin          │──▶ Bff.Admin        ┘
                       └───────────────────────────────────────┘
                          dashboard : http://traefik.localhost (ou :8080)
```

## Démarrer

```bash
cd deploy
cp .env.example .env          # puis renseigne POSTGRES_PASSWORD et JWT_SIGNING_KEY
docker compose up --build
```

Au démarrage, `api` applique les migrations de tous les modules (`RunMigrationsAtStartup=true`),
puis les BFF démarrent une fois l'`api` saine.

## Accès

Deux façons d'atteindre la même surface (les `*.localhost` pointent vers 127.0.0.1) :

| Surface | Par hôte (avec Swagger)        | Par préfixe (gateway unique)      |
|---------|--------------------------------|-----------------------------------|
| Monolithe | http://api.localhost/swagger | http://localhost/api/...          |
| Mobile  | http://mobile.localhost/swagger | http://localhost/mobile/...      |
| Web     | http://web.localhost/swagger   | http://localhost/web/...          |
| Vendeur | http://seller.localhost/swagger | http://localhost/seller/...      |
| Admin   | http://admin.localhost/swagger | http://localhost/admin/...        |

Dashboard Traefik : http://traefik.localhost ou http://localhost:8080.

## Déployer le backend (staging / prod)

Le backend = **Marketplace.Api + 4 BFF** (mobile, web, seller, admin) + Postgres +
Redis + Traefik, décrits dans `docker-compose.staging.yml` / `docker-compose.prod.yml`.
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
cp vault.example.yml group_vars/marketplace/vault.yml
#   → renseigner postgres/neon, jwt, fedapay, r2_*, cloudinary_* …
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

Le VPS ne compile rien : on build+push les 5 images depuis un poste (utile depuis
un Mac arm64, le serveur étant amd64).

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
  up -d --build --force-recreate api bff-seller
```

> Le traitement d'image (Cloudinary) tourne dans `bff-seller` et `api` : ce sont
> ces deux services qu'il faut recréer après avoir renseigné les clés `CLOUDINARY_*`.

### Vérifier / diagnostiquer

```bash
docker compose -f docker-compose.staging.yml ps
docker compose -f docker-compose.staging.yml logs -f --tail=100 api bff-seller
```

## Détourage des photos produit (rembg, local)

Le détourage IA + fond blanc tourne dans un conteneur `rembg` du réseau interne, avec
le modèle **u2net**. Il remplace Cloudinary : pas de quota, pas de facture à l'usage,
et les photos des vendeurs ne quittent pas le serveur.

**Rien à configurer dans le cas courant.** Le service est déclaré dans les compose
staging et prod, et l'API comme les BFF pointent dessus par défaut.

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

## Console vendeur (pro.<domaine>)

Dépôt : `HbaExpress_seller_dashboard`. Elle se déploie comme la console admin — image
construite par GitHub Actions, poussée sur GHCR, tirée par la stack du VPS.

`develop_staging` → staging (`seller-console:staging`) · `develop` → prod (`seller-console:latest`)

### ⚠️ Ordre de la PREMIÈRE mise en service

Ansible lance `docker compose up -d --no-build` sur **toute** la pile. Si l'image
`seller-console` n'est pas encore sur GHCR, le déploiement échoue **et emporte les
autres services**. Dans cet ordre, donc :

1. **DNS** — créer `pro.<domaine>` (A / AAAA vers le VPS). Sans cet enregistrement,
   Let's Encrypt ne peut pas émettre le certificat, et Traefik servira une erreur TLS.
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
cela, un démarrage raté (secret absent, BFF injoignable) remonterait en vert.

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

# La console voit-elle le BFF ? (depuis le réseau interne)
sudo docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec seller-console node -e "require('http').get('http://bff-seller:8080/health',r=>console.log(r.statusCode))"
```

## Notes

- **JWT** : `JWT_SIGNING_KEY` (dans `.env`) est injecté à l'identique dans les 5
  services via `Jwt__SigningKey` — indispensable pour que les jetons émis par
  l'Identity soient validés partout. Les variables d'environnement priment sur
  les `appsettings.json`, donc tes secrets locaux ne sont pas utilisés ici.
- **Base de données** : la chaîne de connexion est surchargée vers le service
  `postgres` (port interne 5432). Le volume `pgdata` persiste les données.
- **Traefik** : `api.insecure: true` et le dashboard sur `:8080` sont réservés au
  développement. Avant un déploiement public : passer en HTTPS (entrypoint
  `websecure` + résolveur ACME), retirer l'accès insecure, et restreindre le CORS
  de la console admin (`Cors:Origins`).
- **Outbox** : chaque service (Api + 4 BFF) embarque les modules et leurs
  processeurs d'arrière-plan. Faire tourner les 5 en parallèle sur la même base
  est pratique en dev ; en production, dédier le traitement d'arrière-plan à un
  seul processus (ou désactiver les workers dans les BFF) pour éviter la
  concurrence sur l'outbox.
