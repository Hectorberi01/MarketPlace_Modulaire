# Bascule de la base vers le VPS dédié

**Objet** : faire passer la production du conteneur `postgres` co-hébergé vers le VPS base joint par WireGuard.
**Durée** : environ trente minutes, dont une interruption de service de cinq à quinze minutes selon la taille de la base.
**Retour arrière** : possible à toutes les étapes jusqu'à la 7, en une commande.

> Ce document a été écrit sans accès aux serveurs. Chaque commande est à relire avant d'être lancée, et les étapes 0 à 4 sont conçues pour être passées **sans rien interrompre** — c'est là qu'on découvre ce qui ne va pas.

---

## Le principe

La bascule tient dans une seule variable :

```yaml
# deploy/ansible/inventories/prod/group_vars/marketplace/main.yml
use_external_database: false   # → true
```

Tout le reste — la chaîne de connexion, le profil qui démarre ou non le conteneur local, le DSN de l'exporter, l'hôte de pgweb — en découle. **Ce drapeau ne migre rien.** Le retourner sans avoir restauré les données ferait démarrer l'API sur une base vide : elle appliquerait ses vingt-six migrations, sèmerait le compte admin, et se déclarerait en bonne santé. La plateforme remonterait vide, sans une seule erreur, pendant que les vraies données dormiraient sur l'autre machine.

C'est pour cela que le drapeau est la dernière étape, jamais la première.

---

## Avant de commencer

Ces trois choses doivent être vraies. Aucune n'est facultative.

**Le vault contient les nouveaux secrets.** `env.j2` référence désormais `delivery_webhook_secret`, `dispatch_outbound_secret` et `dispatch_courier_share` sans valeur par défaut — Jinja échouera en les nommant si elles manquent.

```bash
cd deploy/ansible
ansible-vault edit inventories/prod/group_vars/all/vault.yml
```

À y ajouter, au minimum : les trois ci-dessus, `admin_session_secret`, `registry_token`, `db_admin_readonly_password`, `wg_app_private_key`, `wg_db_private_key`. Le gabarit `inventories/prod/vault.example.yml` liste les vingt-neuf avec, pour chacune, sa raison d'être et la commande qui la génère.

**Les clés publiques WireGuard sont renseignées** dans `inventories/prod/group_vars/all/main.yml` — elles ne sont pas secrètes, et les avoir en clair est ce qui permet de vérifier que les deux côtés se correspondent sans déchiffrer quoi que ce soit.

**Les collections Ansible sont à jour** — `community.postgresql` et `community.general` sont nouvelles.

```bash
ansible-galaxy collection install -r requirements.yml
```

---

## 0. Vérifier ce qui tourne, avant de toucher à quoi que ce soit

```bash
cd deploy/ansible
ansible -i inventories/prod/hosts.ini all -m ping --ask-vault-pass
```

Les deux machines doivent répondre. Si `prod-db-1` ne répond pas, c'est le SSH vers `51.255.40.214` qu'il faut régler d'abord — le reste du runbook en dépend.

```bash
# Sur le VPS applicatif : la taille de ce qu'on va déplacer
ssh root@193.168.145.162 \
  'cd /opt/marketplace && docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
   exec -T postgres psql -U postgres -d HbaExpressDb -c \
   "SELECT pg_size_pretty(pg_database_size(current_database()));"'
```

Notez ce chiffre. Il donne l'ordre de grandeur de l'interruption, et il servira de contrôle à l'étape 5.

**Et relevez les noms réels de la base et du rôle** — c'est ce qui décide de la suite :

```bash
ssh root@193.168.145.162 \
  'grep -E "^(POSTGRES_DB|POSTGRES_USER)=" /opt/marketplace/deploy/.env.prod'
```

> Ces deux noms n'étaient définis nulle part dans l'inventaire : `env.j2` retombait sur ses défauts, `marketplace` et `postgres`. Les valeurs de `app_db_name` / `app_db_user` (`group_vars/all/main.yml`) décrivent la base **cible**, sur le VPS dédié — et c'est vers ces noms-là que l'étape 5 restaure. Si vous voulez conserver les noms actuels, alignez `app_db_name` / `app_db_user` dessus **avant** l'étape 2 ; sinon, la restauration crée bien les objets sous les nouveaux noms, mais vérifiez que rien d'extérieur (un script d'astreinte, une sauvegarde ancienne) ne suppose les anciens.
>
> Ces variables sont délibérément distinctes de `postgres_db` / `postgres_user`, qui pilotent le conteneur local : les confondre aurait fait générer un `.env.prod` demandant un rôle que le volume `pgdata` existant ne connaît pas — plateforme à terre, au prochain déploiement, sans avoir rien basculé.

---

## 1. Le tunnel, repris par Ansible

Le tunnel existe déjà. Ce rôle le décrit, le compare, et ne recharge que si la configuration diffère — par `wg syncconf`, qui n'interrompt aucune connexion établie.

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --tags wireguard --ask-vault-pass --check --diff
```

**`--check --diff` d'abord.** Il montre ce qui changerait sans rien changer. Si le diff sur `/etc/wireguard/wg0.conf` est vide des deux côtés, votre configuration manuelle correspond déjà à ce que décrit le rôle — bonne nouvelle, et vous pouvez passer l'application réelle.

S'il y a un diff, lisez-le avant. Un écart sur `PublicKey` veut dire que les clés du vault ne sont pas celles en service : corrigez le vault, pas le serveur.

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml --tags wireguard --ask-vault-pass
```

Le rôle se termine par un `ping` du pair. S'il échoue, il dit dans quel ordre chercher.

---

## 2. Provisionner la base — sans rien y mettre

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit database --tags database --ask-vault-pass
```

Ce que ce rôle fait : génère la locale `fr_FR.UTF-8`, dépose `conf.d/10-marketplace.conf`, ouvre `pg_hba` au seul réseau du tunnel, crée le rôle applicatif, et crée les bases `HbaExpressDb` et `dispatch` **avec la collation française**.

> **La collation est le seul point de non-retour de ce runbook.** Votre cluster existe déjà, donc *sa* collation est figée depuis son `initdb`. Mais la collation est une propriété de la **base**, pas du cluster : `TEMPLATE template0` permet d'en choisir une autre. C'est la seule raison pour laquelle il est encore temps — et ça ne le sera plus une fois les données dedans.
>
> Le rôle se termine par une vérification qui échoue si la base existait déjà avec une autre collation. Si elle échoue, ne forcez pas : supprimez la base vide (`DROP DATABASE`) et relancez.

**Contrôle.** Depuis le VPS applicatif, la base doit répondre par le tunnel — et seulement par lui :

```bash
ssh root@193.168.145.162 'PGPASSWORD=<mot_de_passe> psql -h 10.20.0.2 -U HbaExpressUser -d HbaExpressDb -c "\l"'
```

Et depuis n'importe où ailleurs, elle ne doit **pas** répondre :

```bash
psql -h 51.255.40.214 -U HbaExpressUser -d HbaExpressDb    # doit expirer, pas demander un mot de passe
```

Si cette seconde commande demande un mot de passe, `listen_addresses` n'a pas été appliqué : arrêtez-vous là.

---

## 3. La sauvegarde, avant tout le reste

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit database --tags backup --ask-vault-pass -e backup_run_now=true
```

> `-e backup_run_now=true` est explicite, et le défaut est `false` : un `pg_dump` complet des deux bases suivi d'un envoi vers R2 n'a rien d'idempotent, et le déclencher à chaque passage du playbook coûterait le temps du dump et la bande passante à chaque déploiement.

Le rôle installe le script, sa configuration, sa rotation de journal, deux tâches cron — et **exécute une première sauvegarde tout de suite**. Il ne se termine pas sur « cron posé, on verra dans six heures » : si la configuration est fausse, vous le savez maintenant.

Cette sauvegarde-là porte sur la base **vide** du VPS base. Son intérêt n'est pas son contenu : c'est de prouver que la chaîne fonctionne de bout en bout — dump, contrôle de lisibilité, envoi vers R2 — avant qu'on en ait besoin pour de vrai.

```bash
ssh root@51.255.40.214 'ls -la /var/backups/marketplace/'
```

---

## 4. Le dump de la production

C'est ici que l'interruption commence. Tout ce qui précède était sans effet sur le service.

```bash
ssh root@193.168.145.162
cd /opt/marketplace

# 4a. Arrêter ce qui écrit. Postgres reste debout ; l'application, non.
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  stop api bff-client bff-seller bff-admin bff-driver dispatch
```

> **Arrêter les applications, pas Postgres.** Un `pg_dump` sur une base qui reçoit encore des écritures est cohérent — Postgres le garantit — mais il photographie un instant qui n'est pas celui de la bascule : les commandes passées entre le dump et le basculement seraient perdues. Couper les écrivains d'abord, c'est la seule façon d'avoir un dump *final*.

```bash
# 4b. Les rôles, puis les deux bases.
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec -T postgres pg_dumpall -U postgres --globals-only > /tmp/globals.sql

docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec -T postgres pg_dump -U postgres -d HbaExpressDb --format=custom --compress=6 \
  > /tmp/HbaExpressDb.dump

# Si une base dispatch existe déjà localement :
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec -T postgres pg_dump -U postgres -d dispatch --format=custom --compress=6 \
  > /tmp/dispatch.dump 2>/dev/null || echo "pas de base dispatch locale — normal"

# 4c. Le dump se relit-il ? Le contrôle le moins cher qui existe.
pg_restore --list /tmp/HbaExpressDb.dump | head
ls -la /tmp/*.dump /tmp/globals.sql
```

Si `pg_restore --list` échoue ou si le fichier fait quelques centaines d'octets, **arrêtez-vous** : le dump est inutilisable, et relancer l'application vous ramène exactement où vous étiez.

```bash
# 4d. Vers le VPS base, par le tunnel.
scp /tmp/HbaExpressDb.dump /tmp/globals.sql root@10.20.0.2:/tmp/
```

---

## 5. La restauration

```bash
ssh root@51.255.40.214
cd /tmp

# 5a. Les rôles d'abord. Sans eux, la base restaurée n'a pas de compte applicatif.
#     Les « already exists » sont normaux : le rôle applicatif vient d'être créé
#     par Ansible.
psql -U postgres -f globals.sql 2>&1 | grep -v "already exists" || true

# 5b. Les données. `--jobs 4` parallélise : sur vingt-six schémas, c'est
#     plusieurs minutes de gagnées.
# Remplacer les deux noms par ceux d'`app_db_name` / `app_db_user`.
sudo -u postgres pg_restore -U postgres -d HbaExpressDb \
  --no-owner --role=HbaExpressUser --jobs 4 --exit-on-error \
  /tmp/HbaExpressDb.dump
```

> **`--exit-on-error`, et c'est délibéré.** Par défaut, `pg_restore` continue après une erreur et se termine en annonçant le nombre d'erreurs — sur un terminal que personne ne relit en entier. Une base restaurée à 97 % est le pire des états : elle démarre, elle répond, et il manque des lignes. Mieux vaut un échec franc.

**Le contrôle qui compte.** Comparez les volumes, table par table, avec ce que vous aviez :

```bash
sudo -u postgres psql -U postgres -d HbaExpressDb -c "
SELECT schemaname, relname, n_live_tup
FROM pg_stat_user_tables
WHERE n_live_tup > 0
ORDER BY n_live_tup DESC
LIMIT 25;"

# Et les trois qui portent l'argent :
sudo -u postgres psql -U postgres -d HbaExpressDb -c "
SELECT 'commandes' AS quoi, count(*) FROM ordering.orders
UNION ALL SELECT 'paiements',   count(*) FROM payments.payments
UNION ALL SELECT 'portefeuilles', count(*) FROM settlement.wallets;"
```

Ces trois chiffres doivent correspondre à ceux de la base d'origine. Prenez-les **avant** l'étape 4 si vous ne l'avez pas fait — c'est le seul contrôle qui distingue une restauration réussie d'une restauration plausible.

```bash
# 5c. L'extension de statistiques, que le dump ne transporte pas.
sudo -u postgres psql -U postgres -d HbaExpressDb \
  -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"

# 5d. Les statistiques du planificateur. Sans ANALYZE, la base restaurée n'a
#     aucune statistique : les premiers plans de requête sont catastrophiques, et
#     l'application semble lente sans raison pendant des heures.
sudo -u postgres psql -U postgres -d HbaExpressDb -c "ANALYZE;"
```

---

## 6. Basculer

```bash
# Sur votre poste
cd deploy/ansible
```

```yaml
# inventories/prod/group_vars/marketplace/main.yml
use_external_database: true
```

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --tags deploy --ask-vault-pass --check --diff
```

Le diff sur `.env.prod` doit montrer exactement trois choses : `MARKETPLACE_CONNECTION` qui passe de `Host=postgres` à `Host=10.20.0.2`, les trois `POSTGRES_*` qui disparaissent, et `COMPOSE_PROFILES` qui se vide. **S'il montre autre chose, ne poursuivez pas.**

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml --tags deploy --ask-vault-pass
```

---

## 7. Vérifier, et seulement ensuite nettoyer

```bash
ssh root@193.168.145.162
cd /opt/marketplace

# Le conteneur postgres ne doit plus apparaître.
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod ps

# L'API répond, et sa sonde de disponibilité teste réellement la base.
curl -fsS https://api.hba-express.org/health/ready && echo "  ← prêt"

# Aucun conteneur en boucle.
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod ps \
  --format json | grep -i "restarting\|exited" && echo "PROBLÈME" || echo "tout est stable"
```

Puis les contrôles métier, qui sont les seuls à prouver que ce sont bien **vos** données : ouvrez la console admin, vérifiez qu'une commande connue est là, qu'un solde vendeur est le bon, et qu'une recherche produit renvoie des résultats.

**Ne supprimez rien avant vingt-quatre heures.** Le volume `pgdata` du VPS applicatif est votre retour arrière ; il ne coûte que de l'espace disque.

```bash
# Après vingt-quatre heures de fonctionnement normal, et pas avant :
docker volume rm marketplace_pgdata
```

---

## Le retour arrière

**Jusqu'à l'étape 6 incluse**, il tient en une ligne : remettre `use_external_database: false` et relancer `--tags deploy`. Le conteneur local redémarre sur son volume `pgdata`, qui n'a pas bougé — les données y sont exactement comme à l'étape 4.

La seule chose perdue est ce qui aurait été écrit sur le VPS base entre-temps. D'où l'intérêt de faire les contrôles de l'étape 7 **avant** de rouvrir le service aux clients.

**Après suppression du volume**, le retour arrière passe par la sauvegarde — c'est-à-dire par la chaîne que l'étape 3 a précisément servi à valider.

---

## Ce que cette bascule ne règle pas

Pour mémoire, et pour ne pas croire le chantier terminé :

- **L'archivage WAL reste désactivé.** `wal_level = replica` le prépare sans l'activer : il n'y a pas de restauration à un instant précis, et le point de reprise est le dernier dump. Six heures, dans le pire cas.
- **La supervision de la base**, sur le VPS applicatif, la joint maintenant par le tunnel — mais il n'existe toujours **aucune règle d'alerte Postgres** dans `prometheus-rules.yml`. Un exporter qui mesure une base que personne n'écoute.
- **Le canal d'alerte de production est vide.** Alertmanager tourne avec des receivers sans destinataire et abandonne chaque notification en silence. Tant que ce n'est pas réglé, la surveillance de sauvegarde de l'étape 3 ne tient que par l'e-mail de cron — qui est d'ailleurs, pour cette raison, volontairement indépendant de Prometheus.
- **Les secrets publiés en clair sur GitHub** ne sont pas concernés par ce runbook et restent la priorité absolue.

Voir `docs/AUDIT_Deploiement.md`, section 7, pour l'ordre complet.
