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
