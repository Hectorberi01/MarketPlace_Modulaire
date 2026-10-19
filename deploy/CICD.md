# CI/CD — staging & prod (backend, console admin, console vendeur, vitrine)

Quatre dépôts, quatre pipelines GitHub Actions, même convention de branches :

| Branche poussée   | Environnement | Étiquette mobile |
|-------------------|---------------|------------------|
| `develop_staging` | **staging**   | `staging`        |
| `develop`         | **prod**      | `latest`         |

Registre commun : `ghcr.io/hectorberi01`. Le VPS ne fait que **pull**
(`use_registry: true`).

## Qui déploie quoi

- **Backend** (`marketPlace` → `.github/workflows/deploy.yml`) : contexte →
  garde-fou .NET (build + tests) → build & push de **l'image unique `api`**
  (`Marketplace.Host`) → **Ansible** `site.yml --tags docker,setup,security,deploy`
  sur l'inventaire de l'environnement. Ansible redéploie **toute la pile** (donc
  aussi `console`, `seller-console` et `site`, en tirant leurs étiquettes
  courantes).
- **Console admin** (`Admin_MP_Next`) : build + push `console:<tag>` → **SSH**
  `docker compose pull console && up -d console`.
- **Console vendeur** (`HbaExpress_seller_dashboard`) : idem pour `seller-console`.
- **Vitrine** (`hbaexpress-site`) : idem pour `site`.

Un push qui ne touche que de la documentation (`**.md`, `docs/**`) ne déclenche
rien côté backend.

## Deux étiquettes, et c'est la seconde qui compte

Chaque build pousse l'image `api` deux fois :

| Étiquette | Rôle |
|---|---|
| `latest` / `staging` | **mobile** — suit la dernière livraison. Pratique à la main, ne désigne aucun commit. |
| `sha-<12 caractères>` | **immuable** — c'est elle qui est **déployée**. Elle dit quel commit tourne, et se redéploie sans rebuild. |

Le backend a sa **propre** variable de tag, `API_IMAGE_TAG`, distincte de
`IMAGE_TAG` que partagent les consoles et la vitrine. Sans cela, redéployer le
backend sur un `sha-…` ferait chercher une `console` à ce même tag, qui n'existe
pas — les consoles sont construites par d'autres dépôts. Le repli est résolu dans
`env.j2` (`api_image_tag | default(image_tag)`) et non dans le compose, parce que
Docker Compose n'imbrique pas les valeurs par défaut de façon fiable.

## Retour arrière (et déploiement manuel)

Actions → **Backend · build & deploy** → *Run workflow* :

| Champ | Valeur |
|---|---|
| `environnement` | `staging` ou `prod` |
| `image_tag` | l'étiquette `sha-…` à remettre en service — **vide** pour construire le commit courant |

Un `image_tag` renseigné saute la compilation **et** le build d'image : le
workflow se contente de vérifier que l'image existe sur GHCR, puis de la
déployer. Pas de revert de commit, pas de reconstruction.

Les étiquettes disponibles se lisent sur la page du package GHCR, ou dans le
récapitulatif de chaque exécution du workflow (il affiche l'image déployée et le
commit).

En direct sur le serveur, le même résultat :

```bash
cd deploy/ansible
ansible-playbook -i inventories/prod/hosts.ini site.yml --tags deploy \
  -e api_image_tag=sha-1a2b3c4d5e6f
```

## Secrets GitHub par dépôt

> Les secrets ci-dessous sont attendus dans un **environnement GitHub nommé
> `deploy`** (Settings → Environments → deploy) ; les jobs concernés déclarent
> `environment: deploy`. À défaut, les créer en *Repository secrets* : ils sont
> alors visibles quel que soit l'environnement du job.

**marketPlace**

| Secret | Rôle |
|---|---|
| `GHCR_TOKEN` | PAT classique `write:packages`. Le job échoue d'emblée s'il manque. |
| `DEPLOY_SSH_KEY_STAGING` | clé privée SSH du VPS de recette |
| `DEPLOY_SSH_KEY_PROD` | clé privée SSH du VPS de production |
| `DEPLOY_SSH_KEY` | clé commune, utilisée **en repli** si la clé propre à l'environnement n'existe pas |
| `ANSIBLE_VAULT_PASSWORD` | mot de passe du vault Ansible |

Une clé par environnement est préférable : deux serveurs, deux hébergeurs, deux
utilisateurs. Avec un secret unique, réparer l'accès à la recette cassait la
production en silence. Aucun repli d'un environnement sur l'autre n'est prévu.

**Admin_MP_Next**, **HbaExpress_seller_dashboard** et **hbaexpress-site**

Secrets de l'environnement GitHub **deploy**, que le job de déploiement déclare :

| Secret | Rôle |
|---|---|
| `DEPLOY_SSH_KEY_PROD` / `DEPLOY_SSH_KEY_STAGING` | clé privée de déploiement, une par cible. `DEPLOY_SSH_KEY` sert de repli commun. |
| `ANSIBLE_VAULT_PASSWORD` | déchiffre le vault de l'inventaire. |
| `GHCR_TOKEN` | lecture de GHCR ; `GITHUB_TOKEN` prend le relais s'il est absent. |

Plus de `DEPLOY_SSH_HOST`, `DEPLOY_SSH_USER` ni `DEPLOY_SSH_PORT` : l'hôte,
l'utilisateur et le port vivent dans `inventories/<env>/hosts.ini`, à un seul
endroit, lu aussi bien par la CI que depuis un poste.

Pas de `DEPLOY_PATH` : le workflow calcule le dossier tout seul —
`/opt/marketplace` (prod) ou `/opt/marketplace-staging` (staging), là où Ansible
a déposé la pile.

Le push sur GHCR utilise le `GITHUB_TOKEN` intégré **si** les packages sont liés
au dépôt. Si les packages existent déjà (poussés en local sous ton compte), le
`GITHUB_TOKEN` reçoit un **403** : soit donner au dépôt l'accès *Write* sur chaque
package (Package settings → Manage Actions access), soit fournir `GHCR_TOKEN`
(les workflows le préfèrent au `GITHUB_TOKEN` quand il est présent).

## Prérequis (une fois)

1. **Branches** : `develop_staging` et `develop` dans chacun des dépôts.
2. **Accès GHCR depuis le VPS** : si les packages sont privés, le serveur doit
   être connecté au registre — géré par le rôle Ansible via `registry_username` /
   `registry_token` du vault. Sinon, rendre les packages publics.
3. **Variables runtime de la vitrine** (vault / `.env` de la pile) :
   `SITE_RESEND_API_KEY`, `SITE_TURNSTILE_SECRET_KEY`, `SITE_CONTACT_TO`,
   `SITE_CONTACT_FROM`.
4. **Variable de BUILD de la vitrine** : `NEXT_PUBLIC_TURNSTILE_SITE_KEY` est
   inlinée au build Next. Elle doit être injectée au `docker build`
   (`--build-arg` + `ARG`/`ENV` dans son Dockerfile), pas au runtime.
5. **Secrets de session des consoles** : `ADMIN_SESSION_SECRET` et
   `seller_session_secret` dans le vault de chaque environnement. Deux valeurs
   distinctes : deux surfaces, deux compromissions indépendantes.
6. **Origines CORS** : `CLIENT_CORS_ORIGINS`, `SELLER_CORS_ORIGINS`,
   `ADMIN_CORS_ORIGINS` — une liste par surface. Vide = aucune origine acceptée,
   et le navigateur bloque. Voir `ENV-PROD.md` § 7.

## Vault chiffré versionné (obligatoire pour « Ansible depuis la CI »)

La CI n'a que le dépôt : Ansible doit y trouver le **vault chiffré**, sinon toutes
les variables secrètes (ex. `jwt_signing_key`) sont *undefined*. Le `.gitignore`
bloque `**/vault.yml` par défaut, mais **dé-ignore** explicitement les deux vaults
d'inventaire (staging/prod) : chiffrés AES-256, ils sont sûrs à versionner —
inutiles sans le mot de passe, qui reste le secret GitHub
`ANSIBLE_VAULT_PASSWORD`.

> Ne versionner que si le fichier commence par `$ANSIBLE_VAULT`. C'est ce que
> vérifie `scripts/verifier-vaults.sh`, exécuté par le workflow à chaque
> déploiement : un `ansible-vault decrypt` oublié ne peut plus passer au commit.
> Et s'assurer qu'aucune clé compromise n'y traîne — révoquer avant.
>
> Le vault de PROD est dans `group_vars/all/` (et non `group_vars/marketplace/`) :
> le rôle `backup` tourne sur le groupe `database` et lirait « undefined » sinon.

Committer chaque vault sur SA branche :

```bash
# Prod → branche develop
git checkout develop
git add .gitignore deploy/ansible/inventories/prod/group_vars/all/vault.yml
git commit -m "ci(deploy): versionner le vault prod chiffré"
git push origin develop

# Staging → branche develop_staging
git checkout develop_staging
git add .gitignore deploy/ansible/inventories/staging/group_vars/marketplace/vault.yml
git commit -m "ci(deploy): versionner le vault staging chiffré"
git push origin develop_staging
```

## Notes

- **Ansible ne touche plus aux consoles ni à la vitrine.** Il ne tire que `api`
  (`registry_images`) et ne démarre que `backend_services`. Les trois services Next
  restent déclarés dans les fichiers compose — les en retirer les ferait supprimer
  par `--remove-orphans` — mais leur déploiement appartient au dépôt web.
- Le redeploy SSH des consoles suppose l'arborescence posée par le rôle Ansible :
  pile dans `/opt/marketplace` (prod) ou `/opt/marketplace-staging` (staging), avec
  `deploy/docker-compose.<env>.yml` et `deploy/.env.<env>`.
- Le VPS est en `linux/amd64` : toutes les images sont construites pour cette
  plateforme (workflows et `build-and-push.sh`).
- `build-and-push.sh` reste le chemin **local** (depuis un Mac, par exemple). La
  CI passe par `docker/build-push-action` pour le cache GitHub Actions. Les deux
  utilisent le même Dockerfile et les mêmes `--build-arg` : s'ils changent, changer
  les deux.
- **Le VPS base n'est pas dans le périmètre de la CI** (`--limit marketplace`) : la
  clé de déploiement n'est pas dans ses `authorized_keys`. Il se pilote depuis un
  poste avec `--limit database`.
