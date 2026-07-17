# Déploiement Ansible — Marketplace

Déploie la stack Docker Compose (Marketplace.Api + 4 BFF + Redis + Traefik) sur
**dev**, **staging** et **prod** depuis une seule base de code. Les secrets sont
gérés avec **Ansible Vault**, le reste via `group_vars`.

## Prérequis (machine de contrôle)

```bash
pip install ansible
cd deploy/ansible
ansible-galaxy collection install -r requirements.yml   # ansible.posix, community.docker
```

Les hôtes staging/prod doivent être Debian/Ubuntu, accessibles en SSH (clé), avec
un utilisateur sudo (`ansible_user`). Docker y est installé automatiquement.

## Structure

```
ansible/
  site.yml                     # playbook : rôle docker + rôle marketplace
  inventories/<env>/
    hosts.ini                  # hôtes de l'environnement
    group_vars/marketplace/
      main.yml                 # variables non sensibles
      vault.yml                # SECRETS (chiffré Ansible Vault) — à créer
    vault.example.yml          # gabarit des secrets (staging/prod)
  roles/
    docker/                    # installation Docker + Compose (Debian/Ubuntu)
    marketplace/               # sync code, rendu .env, docker compose up --build
```

Chaque environnement pointe vers son fichier compose :
`docker-compose.yml` (dev), `docker-compose.staging.yml`, `docker-compose.prod.yml`.

## Préparer les secrets (staging / prod)

```bash
cd deploy/ansible/inventories/staging          # ou prod
cp vault.example.yml group_vars/marketplace/vault.yml
# renseigner Neon, Redis, JWT, FedaPay, R2…
ansible-vault encrypt group_vars/marketplace/vault.yml
# édition ultérieure : ansible-vault edit group_vars/marketplace/vault.yml
```

Renseigner aussi `hosts.ini` (IP/host, `ansible_user`) et `group_vars/marketplace/main.yml`
(`root_domain`, `acme_email`, buckets R2…).

> Le DNS des 5 sous-domaines (`api.` `m.` `web.` `seller.` `admin.$ROOT_DOMAIN`)
> doit pointer vers le serveur AVANT le 1er déploiement (émission Let's Encrypt).

## Déployer

```bash
cd deploy/ansible

# DEV (machine locale, Docker Desktop) — pas de vault, pas d'install Docker
ansible-playbook -i inventories/dev/hosts.ini site.yml

# STAGING
ansible-playbook -i inventories/staging/hosts.ini site.yml --ask-vault-pass

# PROD
ansible-playbook -i inventories/prod/hosts.ini site.yml --ask-vault-pass
```

Options utiles :
- `--tags deploy` : redéploie sans réinstaller Docker (build + up seulement).
- `--check` : simulation (dry-run).
- `--vault-password-file ~/.vault_pass` : évite `--ask-vault-pass`.

## Ce que fait le playbook

1. **rôle docker** : installe Docker Engine + plugin Compose (sauf dev).
2. **rôle marketplace** :
   - synchronise le code source vers `{{ app_dir }}` (rsync, hors `bin/obj/.git/.env*`) ;
   - génère `deploy/.env.<env>` depuis les variables + le vault ;
   - lance `docker compose -f <compose_file> --env-file .env.<env> up -d --build --remove-orphans`.

L'API applique les migrations EF au démarrage (`RunMigrationsAtStartup=true`).

## Notes

- Ne **jamais** committer un `vault.yml` en clair : toujours `ansible-vault encrypt`.
- Staging utilise l'ACME **staging** de Let's Encrypt (certs non fiables, sans
  rate-limit) ; basculer `acme_caserver` vers l'URL prod pour des certs fiables.
- Les dashboards Blazor (Seller/Admin) sont des SPA WASM hébergées séparément :
  non gérées par ce playbook.
