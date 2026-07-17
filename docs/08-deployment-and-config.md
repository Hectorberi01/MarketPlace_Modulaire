# 08 — Configuration & déploiement

Détail des commandes de déploiement : **`deploy/README.md`**. Cette page récapitule les **clés de
configuration** et **variables d'environnement** que consomme le backend.

## 1. Priorité de configuration

`appsettings.json` (défauts) **<** variables d'environnement (surcharge en conteneur). Convention .NET :
la section `A:B:C` se surcharge par `A__B__C`. En prod, **les secrets ne vivent qu'en variables d'env**
(générées depuis le vault Ansible), jamais dans un fichier suivi.

## 2. Sections de configuration

| Section / clé | Rôle | Env (conteneur) |
|---------------|------|-----------------|
| `ConnectionStrings:Marketplace` | Postgres (partagé, schéma par module) | `ConnectionStrings__Marketplace` |
| `RunMigrationsAtStartup` | migrations + seed au boot (Api) | `RunMigrationsAtStartup` |
| `Redis:ConnectionString` / `Redis:InstanceName` | cache distribué + backplane SignalR | `Redis__ConnectionString` |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:SigningKey` | JWT (même clé sur les 5 hôtes) | `Jwt__SigningKey` (**requis**) |
| `Cors:Origins` | origines autorisées (Admin/Web) | `Cors__Origins__0`, `…__1` |
| `Payments:FedaPay:ApiKey` / `WebhookSecret` / `EnablePayouts` | Mobile Money (paiements + payouts) | `Payments__FedaPay__*` / `FEDAPAY_*` |
| `Storage:CloudflareR2:*` | R2 privé (pièces KYB) | `Storage__CloudflareR2__*` / `R2_*` |
| `Storage:CloudflareR2Media:*` | R2 public (images produit) | `Storage__CloudflareR2Media__*` / `R2_MEDIA_*` |
| `Media:Cloudinary:{CloudName,ApiKey,ApiSecret,MaxWaitSeconds}` | traitement image (détourage) | `Media__Cloudinary__*` / `CLOUDINARY_*` |
| `Media:BaseUrl` / `Media:ClientId` | ancien service média (repli) | `Media__*` |
| `AdminSeed:*` / `SellerSeed:*` | comptes de seed (dev) | idem |
| `OUTBOX_ENABLED` | active/désactive l'`OutboxProcessor` sur un hôte | `OUTBOX_ENABLED` |

Chaque secret non renseigné dégrade proprement : R2 → stub média ; Cloudinary → `NullImageProcessor` (image
inchangée) ; FedaPay `EnablePayouts=false` → payout simulé.

## 3. Environnements

- **dev** : `docker-compose.yml`, machine locale, pas de vault (voir `deploy/README.md`).
- **staging** : `docker-compose.staging.yml`, `.env.staging` généré depuis le vault
  (`inventories/staging/group_vars/marketplace/vault.yml`, chiffré Ansible-Vault) via `env.j2`.
- **prod** : `docker-compose.prod.yml`, `.env.prod`.

## 4. Déploiement (résumé)

Deux voies (détail dans `deploy/README.md`) :

1. **Ansible** (recommandé) — sync code, rend `.env.<env>` depuis le vault, `docker compose up` :
   ```bash
   cd deploy/ansible
   ansible-playbook -i inventories/staging/hosts.ini site.yml --tags deploy --ask-vault-pass
   ```
2. **Registry (pull)** — build local amd64 → push GHCR → le VPS pull :
   ```bash
   IMAGE_PREFIX=ghcr.io/<user> IMAGE_TAG=staging ./deploy/build-and-push.sh
   ```

> Le staging déploie des **images pré-construites** : après un changement de code backend, il faut
> **rebuild+push** puis redéployer (sinon le serveur relivre l'ancienne image). Les migrations EF
> s'appliquent au démarrage de l'`api`.

## 5. Sécurité — rappels

- `vault.yml` doit rester **chiffré** (`ansible-vault encrypt`) et gitignoré.
- Ne jamais committer de secret (Postgres, `Jwt:SigningKey`, FedaPay, R2, Cloudinary, token GHCR).
- Régénérer toute clé exposée. Restreindre Swagger et durcir le CORS avant une exposition publique.
