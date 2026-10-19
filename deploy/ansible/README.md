# Déploiement Ansible — HBA Express

Déploie la pile Docker Compose du backend sur **staging** et **prod**, et configure
le VPS base de la production. Les secrets vivent dans un **Ansible Vault** chiffré ;
tout le reste dans `group_vars`.

Ce dépôt ne déploie QUE le backend. Les trois applications Next (boutique, console
vendeur, console admin) viennent du dépôt web : elles restent déclarées dans les
fichiers compose — les en retirer les ferait supprimer par `--remove-orphans` — mais
Ansible ne les tire pas, ne les démarre pas et ne les arrête pas.

Il n'y a plus d'inventaire `dev` : en local, `docker compose up` depuis `deploy/`.

## Prérequis (machine de contrôle)

```bash
pip install ansible
cd deploy/ansible
ansible-galaxy collection install -r requirements.yml
```

## Structure

```
ansible/
  site.yml                      # 3 plays : tunnel, VPS base, applicatif
  ansible.cfg
  inventories/
    prod/
      hosts.ini                 # 2 groupes : marketplace (app) + database
      group_vars/
        all/main.yml            # commun aux deux groupes (identité base, pool)
        all/vault.yml           # SECRETS, CHIFFRÉS — les deux groupes les lisent
        marketplace/main.yml    # domaines, registry, buckets R2
        database/main.yml       # dimensionnement Postgres, MinIO, sauvegardes
      vault.example.yml
    staging/
      hosts.ini                 # un seul groupe : marketplace
      group_vars/marketplace/{main.yml,vault.yml}
      vault.example.yml
  roles/
    docker/          installation Docker + plugin Compose (Debian/Ubuntu)
    security/        Fail2ban, rsyslog, UFW (opt-in)
    wireguard/       tunnel entre le VPS applicatif et le VPS base
    postgres_host/   PostgreSQL sur le VPS base (écoute sur le tunnel seul)
    minio/           stockage objet des pièces KYB, sur le VPS base
    backup/          pg_dump périodique vers R2
    marketplace/     synchro du code, .env, pull de l'image, compose up, vérification
```

Le vault de production est dans `group_vars/all/` et non `group_vars/marketplace/` :
le rôle `backup` tourne sur le groupe `database` et lirait « undefined » autrement.

## Rôle marketplace

Un fichier par étape, dans `roles/marketplace/tasks/` :

| Fichier | Ce qu'il fait |
|---|---|
| `preflight.yml` | contrôles avant toute écriture sur l'hôte |
| `sync.yml` | rsync du dépôt vers `app_dir` |
| `env.yml` | rendu de `.env.<env>`, compte de service FCM |
| `images.yml` | login registry et pull de l'image `api` |
| `compose.yml` | build ou `up -d`, recréation du Traefik de staging, retrait des Postgres résiduels |
| `postgres-local.yml` | pg_stat_statements, Postgres co-hébergé seulement |
| `verification.yml` | readiness `/health/ready` puis routage public à travers Traefik |

Ce dépôt ne publie qu'une image, `api` (voir `deploy/build-and-push.sh`). Redis,
rembg, Traefik et Postgres viennent de registres publics : `up` les tire lui-même.
En production, le compose ne déclare ni Traefik, ni Redis, ni Postgres : le VPS fait
tourner son propre proxy et son propre Redis, et la base vit sur le VPS dédié. Il ne
reste donc que `api` et `rembg`.

## Préparer les secrets

```bash
cd deploy/ansible/inventories/prod                 # ou staging
cp vault.example.yml group_vars/all/vault.yml      # staging : group_vars/marketplace/
# renseigner : base, redis_password, JWT, platform_secret_key, FedaPay, R2, registry…
ansible-vault encrypt group_vars/all/vault.yml
# édition ultérieure :
ansible-vault edit group_vars/all/vault.yml
```

Un vault ne se modifie QUE par `ansible-vault edit`. Un `decrypt` oublié part en
clair au commit suivant : `scripts/verifier-vaults.sh` refuse un vault suivi non
chiffré, et la CI l'exécute à chaque déploiement.

Le DNS de chaque nom d'hôte servi doit pointer vers le VPS AVANT le premier
déploiement : le certificat est émis par nom (challenge TLS-ALPN).

## Déployer

```bash
cd deploy/ansible

# STAGING
ansible-playbook -i inventories/staging/hosts.ini site.yml --ask-vault-pass

# PROD — applicatif seul (ce que fait la CI)
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit marketplace --tags docker,setup,security,deploy --ask-vault-pass

# PROD — VPS base (depuis un poste : la clé de la CI n'y est pas posée)
ansible-playbook -i inventories/prod/hosts.ini site.yml --limit database --ask-vault-pass
```

Options utiles :

- `--tags deploy` : redéploie sans réinstaller Docker.
- `--check` : simulation.
- `--vault-password-file ~/.vault_pass` : évite `--ask-vault-pass`.
- `-e api_image_tag=sha-xxxxxxxxxxxx` : redéploie une image précise (retour arrière).

## Ce qui n'est pas piloté d'ici

- Le reverse proxy de production : installé à part sur le VPS, sur le réseau `proxy`.
  `traefik_entrypoint`, `traefik_certresolver` et `traefik_proxy_network` nomment
  LES SIENS — un mauvais nom ne se voit qu'en production.
- La supervision : VPS dédié. Les variables `otel_endpoint`, `prometheus_url`,
  `loki_url`, `grafana_url` et `grafana_embed_url` restent vides par défaut.
- Le tunnel WireGuard de production : `enable_wireguard` est faux, le tunnel a été
  posé à la main. Le rôle sait le configurer, mais l'activer réappliquerait sa
  configuration sur un tunnel en service.
- Les migrations EF : l'application les applique au démarrage
  (`RunMigrationsAtStartup=true`).
