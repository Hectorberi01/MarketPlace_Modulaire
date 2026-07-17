# CI/CD — staging & prod (backend, console admin, site vitrine)

Trois dépôts, trois pipelines GitHub Actions, même convention de branches :

| Branche poussée   | Environnement | Tag d'image |
|-------------------|---------------|-------------|
| `develop_staging` | **staging**   | `staging`   |
| `develop`         | **prod**      | `latest`    |

Registre commun : `ghcr.io/hectorberi01`. Le VPS ne fait que **pull** (`use_registry: true`).

## Qui déploie quoi

- **Backend** (`marketPlace` → `.github/workflows/deploy.yml`) : garde-fou .NET
  (build + tests) → `build-and-push.sh` (api + 4 BFF) → **Ansible** `site.yml
  --tags deploy` sur l'inventaire de l'env. Ansible redéploie **toute la stack**
  (donc aussi `console` et `site`, en tirant leurs tags courants).
- **Console admin** (`Admin_MP_Next` → `.github/workflows/deploy.yml`) : build +
  push `console:<tag>` → **SSH** `docker compose pull console && up -d console`.
- **Site vitrine** (`hbaexpress-site` → `.github/workflows/deploy.yml`) : build +
  push `site:<tag>` → **SSH** `docker compose pull site && up -d site`.

> Les secrets ci-dessous sont attendus dans un **environnement GitHub nommé
> `deploy`** (Settings → Environments → deploy). Les workflows déclarent
> `environment: deploy` sur les jobs concernés. (À défaut, les mettre en *Repository
> secrets* et retirer les lignes `environment: deploy`.)

## Secrets GitHub par dépôt

**marketPlace**
- `DEPLOY_SSH_KEY` — clé privée SSH autorisée sur le VPS (correspond à la clé
  publique du serveur ; l'hôte/utilisateur sont dans `inventories/<env>/hosts.ini`).
- `ANSIBLE_VAULT_PASSWORD` — mot de passe du vault Ansible.

**Admin_MP_Next** et **hbaexpress-site**
- Secrets dans **deux environnements GitHub** : `staging` et `prod` (les jobs
  utilisent `environment: <staging|prod>` selon la branche). Chacun contient :
  `GHCR_TOKEN`, `DEPLOY_SSH_HOST`, `DEPLOY_SSH_USER`, `DEPLOY_SSH_KEY`, et
  éventuellement `DEPLOY_SSH_PORT` (défaut **8022**).
- Pas de `DEPLOY_PATH` : le workflow calcule le dossier tout seul —
  `/opt/marketplace` (prod) ou `/opt/marketplace-staging` (staging), là où Ansible
  a déposé la stack. Le redeploy fait
  `docker compose -f deploy/docker-compose.<env>.yml --env-file deploy/.env.<env> pull/up <service>`.

Le push d'images sur GHCR utilise le `GITHUB_TOKEN` intégré **si** les packages sont
liés au dépôt. Si les packages existent déjà (poussés en local sous ton compte), le
`GITHUB_TOKEN` reçoit un **403 Forbidden** : soit donner au dépôt l'accès *Write* sur
chaque package (Package settings → Manage Actions access), soit créer un PAT classique
avec `write:packages` et l'ajouter en secret **`GHCR_TOKEN`** (les workflows le
préfèrent au `GITHUB_TOKEN` s'il est présent).

## Prérequis (une fois)

1. **Branches** : créer `develop_staging` et `develop` dans chacun des 3 dépôts.
2. **hbaexpress-site** n'est pas encore un dépôt Git : `git init`, commit, puis
   `git remote add origin …` (ex. `HbaExpress_site`).
3. **Prod compose incomplet** : `docker-compose.prod.yml` ne contient **ni
   `console` ni `site`**. Les ajouter en miroir du staging (le service `site` vient
   d'être ajouté à `docker-compose.staging.yml` ; copier `console` + `site` en prod,
   avec les sous-domaines réels : `console.${ROOT_DOMAIN}` et `${ROOT_DOMAIN}` /
   `www.${ROOT_DOMAIN}`). Sinon le `pull/up` prod de l'admin et du site échoue.
4. **Accès GHCR depuis le VPS** : si les packages sont privés, le serveur doit être
   connecté au registre (`docker login ghcr.io`) — géré par le rôle Ansible via une
   variable de vault, ou rendre les packages `console`/`site` publics.
5. **Variables runtime du site** (dans le `.env` de la stack / vault) :
   `SITE_RESEND_API_KEY`, `SITE_TURNSTILE_SECRET_KEY`, `SITE_CONTACT_TO`,
   `SITE_CONTACT_FROM`.
6. **Variable de BUILD du site** : `NEXT_PUBLIC_TURNSTILE_SITE_KEY` est inlinée au
   build Next. Elle doit être injectée au `docker build` (via `--build-arg` /
   `ARG`+`ENV` dans le Dockerfile du site), pas au runtime.
7. **Console** : le secret `ADMIN_SESSION_SECRET` (chiffrement de session) doit être
   dans le vault de chaque env (déjà prévu côté staging).

## Vault chiffré versionné (obligatoire pour « Ansible depuis la CI »)

La CI n'a que le dépôt : Ansible doit y trouver le **vault chiffré**, sinon toutes
les variables secrètes (ex. `jwt_signing_key`) sont *undefined*. Le `.gitignore`
bloque `**/vault.yml` par défaut, mais **dé-ignore** explicitement les deux vaults
d'inventaire (staging/prod) : ils sont chiffrés AES-256, donc sûrs à versionner
(inutiles sans le mot de passe, qui reste le secret GitHub `ANSIBLE_VAULT_PASSWORD`).

> ⚠️ Ne versionner que si le fichier commence par `$ANSIBLE_VAULT` :
> `head -1 deploy/ansible/inventories/<env>/group_vars/marketplace/vault.yml`.
> Et s'assurer qu'aucune clé compromise (ex. l'ancienne clé Resend) n'y traîne —
> révoquer/régénérer avant.

Committer chaque vault sur SA branche :

```bash
# Prod → branche develop
git checkout develop
git add .gitignore deploy/ansible/inventories/prod/group_vars/marketplace/vault.yml
git commit -m "ci(deploy): versionner le vault prod chiffré"
git push origin develop

# Staging → branche develop_staging
git checkout develop_staging
git add .gitignore deploy/ansible/inventories/staging/group_vars/marketplace/vault.yml
git commit -m "ci(deploy): versionner le vault staging chiffré"
git push origin develop_staging
```

## Notes

- Comme les 3 services partagent `IMAGE_TAG`, un déploiement **backend prod** tire
  `console:latest` et `site:latest` : veille à ce que ces images aient bien été
  poussées (leurs pipelines respectifs sur `develop`).
- Le redeploy SSH (console/site) suppose l'arborescence posée par le rôle Ansible :
  stack dans `/opt/marketplace` (prod) / `/opt/marketplace-staging` (staging), avec
  `deploy/docker-compose.<env>.yml` et `deploy/.env.<env>`. Ajuster si le rôle change.
- Le VPS est en `linux/amd64` : toutes les images sont buildées pour cette
  plateforme (déjà géré dans les workflows et `build-and-push.sh`).
