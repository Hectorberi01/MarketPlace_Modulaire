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
ssh -p 8022 ubuntu@79.137.35.129 \
  'cd /opt/marketplace && docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
   exec -T postgres psql -U postgres -d HbaExpressDb -c \
   "SELECT pg_size_pretty(pg_database_size(current_database()));"'
```

Notez ce chiffre. Il donne l'ordre de grandeur de l'interruption, et il servira de contrôle à l'étape 5.

**Et relevez les noms réels de la base et du rôle** — c'est ce qui décide de la suite :

```bash
ssh -p 8022 ubuntu@79.137.35.129 \
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
ssh -p 8022 ubuntu@79.137.35.129 'PGPASSWORD=<mot_de_passe> psql -h 10.20.0.2 -U HbaExpressUser -d HbaExpressDb -c "\l"'
```

Et depuis n'importe où ailleurs, elle ne doit **pas** répondre :

```bash
psql -h 51.255.40.214 -U HbaExpressUser -d HbaExpressDb    # doit expirer, pas demander un mot de passe
```

Si cette seconde commande demande un mot de passe, `listen_addresses` n'a pas été appliqué : arrêtez-vous là.

---

## Les valeurs réelles, relevées sur les serveurs

Tout ce qui suit a été **vérifié en septembre 2026**, sur les machines. Les versions précédentes de ce document contenaient des valeurs supposées, dont une adresse IP qui n'est pas la vôtre.

| | valeur |
|---|---|
| VPS applicatif | `ubuntu@79.137.35.129`, port SSH **8022** |
| VPS base | `ubuntu@51.255.40.214`, port SSH **22**, clé `id_ed25519_cicd` |
| Tunnel | applicatif `10.20.0.1` ↔ base `10.20.0.2` — opérationnel |
| Base source | conteneur `marketplace-prod-postgres-1`, base **`marketplace`**, rôle `postgres`, **PostgreSQL 16** |
| Base dispatch source | conteneur `marketplace-prod-postgres-dispatch-1`, base `dispatch` |
| Base cible | `HbaExpressDb`, rôle `HbaExpressUser`, **PostgreSQL 18** |
| Volume de repli | `marketplace_pgdata` sur l'applicatif |

**Trois corrections par rapport à la version précédente de ce document**, chacune capable de faire échouer la bascule :

1. L'adresse de l'applicatif était `79.137.35.129`. **C'est `79.137.35.129`.**
2. Le dump visait la base `HbaExpressDb` **dans le conteneur**. Elle n'y existe pas : la base locale s'appelle `marketplace`, avec le rôle `postgres`. Le renommage se fait **à la restauration**, pas au dump.
3. Le dump du dispatch visait le service `postgres`. Le dispatch a **son propre conteneur** et son propre volume.

Un dump 16 se restaure sans difficulté dans un serveur 18 — c'est le sens normal d'une migration. L'inverse ne serait pas vrai.

---

## 3. La sauvegarde, avant tout le reste

```bash
cd deploy/ansible
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit database --tags backup --ask-vault-pass -e backup_run_now=true
```

> `-e backup_run_now=true` est explicite, et le défaut est `false` : un `pg_dump` complet suivi d'un envoi vers R2 n'a rien d'idempotent, et le déclencher à chaque passage du playbook coûterait le temps du dump et la bande passante à chaque déploiement.

Le rôle installe le script, sa configuration, sa rotation de journal, les tâches cron — et **exécute une première sauvegarde tout de suite**. Si la configuration est fausse, vous le savez maintenant plutôt que dans six heures.

Cette sauvegarde porte sur les bases **vides** du VPS base. Son intérêt n'est pas son contenu : c'est de prouver que la chaîne fonctionne de bout en bout — dump, contrôle de lisibilité, envoi vers R2 — avant qu'on en ait besoin pour de vrai.

**Elle échouera volontairement si les identifiants R2 manquent** : le script traite l'absence d'envoi distant comme une erreur, pas comme un avertissement. Les variables attendues dans le vault sont `r2_account_id`, `r2_backup_access_key_id` et `r2_backup_secret_access_key` — à défaut, `r2_access_key_id` et `r2_secret_access_key`.

```bash
ssh -p 22 ubuntu@51.255.40.214 'sudo ls -lh /var/backups/marketplace/'
```

> **Ce que cette étape répare.** Jusqu'ici, aucune sauvegarde automatique n'avait jamais tourné : le rôle n'avait jamais été appliqué, et le script refuse de s'exécuter ailleurs que sur le VPS base. La seule copie existante de la production est celle prise à la main. Cette étape est donc la première sauvegarde planifiée de l'histoire de la plateforme.

---

## 4. Le dump de la production

C'est ici que l'interruption commence. Tout ce qui précède était sans effet sur le service.

### 4a. Les chiffres de référence — AVANT d'arrêter quoi que ce soit

Sans eux, vous ne pourrez pas distinguer une restauration réussie d'une restauration plausible.

```bash
ssh -p 8022 ubuntu@79.137.35.129
cd /opt/marketplace

sudo docker exec marketplace-prod-postgres-1 psql -U postgres -d marketplace -c "
SELECT 'commandes' AS quoi, count(*) FROM ordering.orders
UNION ALL SELECT 'paiements',    count(*) FROM payments.payments
UNION ALL SELECT 'portefeuilles', count(*) FROM settlement.wallets;"

sudo docker exec marketplace-prod-postgres-1 psql -U postgres -d marketplace -c "
SELECT schemaname, relname, n_live_tup FROM pg_stat_user_tables
WHERE n_live_tup > 0 ORDER BY n_live_tup DESC LIMIT 25;"
```

**Notez ces chiffres ailleurs que dans le terminal.**

### 4b. Arrêter ce qui écrit

Postgres reste debout ; l'application, non.

```bash
C="deploy/docker-compose.prod.yml"; E="deploy/.env.prod"
sudo docker compose -f "$C" --env-file "$E" \
  stop api bff-client bff-seller bff-admin bff-driver dispatch
```

> Un `pg_dump` sur une base qui reçoit encore des écritures est cohérent — Postgres le garantit — mais il photographie un instant qui n'est pas celui de la bascule : les commandes passées entre le dump et le basculement seraient perdues. Couper les écrivains d'abord est la seule façon d'avoir un dump *final*.

### 4c. Les dumps

```bash
H=$(date +%F-%H%M)

# La base de la place de marché. Elle s'appelle « marketplace » ICI — le nom cible
# HbaExpressDb n'apparaît qu'à la restauration.
sudo docker exec marketplace-prod-postgres-1 \
  pg_dump -U postgres -d marketplace --format=custom --compress=6 \
  | sudo tee /var/backups/marketplace-app/marketplace-$H.dump >/dev/null

# Le dispatch — AUTRE CONTENEUR, autre volume.
sudo docker ps --format '{{.Names}}' | grep dispatch
sudo docker exec marketplace-prod-postgres-dispatch-1 \
  pg_dump -U postgres -d dispatch --format=custom --compress=6 \
  | sudo tee /var/backups/marketplace-app/dispatch-$H.dump >/dev/null
```

> **Pas de `globals.sql` à restaurer.** La version précédente de ce document dumpait les rôles pour les rejouer sur la cible. C'est inutile ici — Ansible a déjà créé `HbaExpressUser` avec le mot de passe du vault — et c'est **risqué** : `pg_dumpall --globals-only` contient un `ALTER ROLE postgres … PASSWORD …` qui écraserait le mot de passe du superutilisateur du VPS base. Gardez-en une copie d'archive si vous voulez, ne la rejouez pas.

### 4d. Le dump se relit-il ?

```bash
sudo pg_restore --list /var/backups/marketplace-app/marketplace-$H.dump | wc -l
ls -lh /var/backups/marketplace-app/
```

Si `pg_restore --list` échoue, ou si le fichier fait quelques centaines d'octets, **arrêtez-vous**. Relancer les applications vous ramène exactement où vous étiez.

### 4e. Vers le VPS base, par le tunnel

```bash
sudo chmod 644 /var/backups/marketplace-app/*-$H.dump
scp /var/backups/marketplace-app/marketplace-$H.dump \
    /var/backups/marketplace-app/dispatch-$H.dump \
    ubuntu@10.20.0.2:/tmp/
```

---

## 5. La restauration

```bash
ssh -p 22 ubuntu@51.255.40.214
cd /tmp
```

### 5a. Retirer l'extension de la table des matières — SINON LA RESTAURATION S'ARRÊTE

**Constaté en répétition, le 14/09/2026.** Le dump contient un
`COMMENT ON EXTENSION pg_stat_statements`. Or `--role=HbaExpressUser` fait exécuter tout
le contenu sous `SET ROLE`, et seul le PROPRIÉTAIRE de l'extension — `postgres` — peut la
commenter. L'erreur tombe, `--exit-on-error` arrête net, et la base reste à moitié
restaurée :

```
pg_restore: error: could not execute query: ERROR:  must be owner of extension pg_stat_statements
```

En pleine bascule, cela veut dire une interruption qui s'allonge pendant qu'on cherche.

On ne baisse pas la garde pour autant : ni `--no-comments`, qui supprimerait TOUS les
commentaires du schéma, ni le retrait d'`--exit-on-error`, qui laisserait passer une
restauration partielle. On retire l'extension de la table des matières — elle existe déjà
côté cible, posée par Ansible, donc il n'y a rien à perdre.

```bash
sudo -u postgres pg_restore -l /tmp/marketplace-*.dump > /tmp/toc.list
grep -v "pg_stat_statements" /tmp/toc.list > /tmp/toc-filtre.list
```

### 5b. Les données de la place de marché

```bash
# --no-owner : le dump porte « postgres » comme propriétaire, la cible veut
# HbaExpressUser. --role fait la bascule de propriété à l'insertion.
sudo -u postgres pg_restore -d HbaExpressDb \
  --no-owner --role=HbaExpressUser --jobs 4 --exit-on-error \
  -L /tmp/toc-filtre.list \
  /tmp/marketplace-*.dump

# Le dispatch — même filtre si son dump porte l'extension.
sudo -u postgres pg_restore -d dispatch \
  --no-owner --jobs 2 --exit-on-error \
  /tmp/dispatch-*.dump
```

> **`--exit-on-error`, et c'est délibéré.** Par défaut, `pg_restore` continue après une erreur et se termine en annonçant le nombre d'erreurs — sur un terminal que personne ne relit en entier. Une base restaurée à 97 % est le pire des états : elle démarre, elle répond, et il manque des lignes. Mieux vaut un échec franc.

> **Si la restauration s'arrête en cours de route**, la base est dans un état partiel —
> avec `--jobs 4`, une partie du travail a pu aboutir. Repartez propre, ne reprenez pas :
>
> ```bash
> sudo -u postgres psql <<'SQL'
> DROP DATABASE IF EXISTS "HbaExpressDb";
> CREATE DATABASE "HbaExpressDb" OWNER "HbaExpressUser";
> SQL
> sudo -u postgres psql -d HbaExpressDb -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"
> ```

### Le contrôle qui compte

**`ANALYZE` D'ABORD, ET C'EST L'ORDRE QUI COMPTE.** `n_live_tup` est une ESTIMATION
statistique : après une restauration fraîche, elle vaut zéro tant qu'`ANALYZE` n'a pas
tourné, même sur une base pleine. Une version précédente de ce document plaçait `ANALYZE`
après ce contrôle — on y lisait donc des zéros partout, on en concluait à un échec, et on
recommençait une restauration parfaitement valide, en pleine interruption.

```bash
sudo -u postgres psql -d HbaExpressDb -c "ANALYZE;"
sudo -u postgres psql -d dispatch      -c "ANALYZE;"
```

Les `count(*)` ci-dessous, eux, sont exacts et ne dépendent pas des statistiques. Ce sont
les mêmes requêtes qu'à l'étape 4a : **les chiffres doivent correspondre.**

```bash
sudo -u postgres psql -d HbaExpressDb -c "
SELECT 'commandes' AS quoi, count(*) FROM ordering.orders
UNION ALL SELECT 'paiements',    count(*) FROM payments.payments
UNION ALL SELECT 'portefeuilles', count(*) FROM settlement.wallets;"

sudo -u postgres psql -d HbaExpressDb -c "
SELECT schemaname, relname, n_live_tup FROM pg_stat_user_tables
WHERE n_live_tup > 0 ORDER BY n_live_tup DESC LIMIT 25;"
```

### 5c. Ce que le dump ne transporte pas

```bash
# L'extension de statistiques.
sudo -u postgres psql -d HbaExpressDb -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"

# (ANALYZE a déjà été lancé plus haut — voir « Le contrôle qui compte ». Sans lui, la
# base restaurée n'a aucune statistique : les premiers plans de requête sont
# catastrophiques et l'application semble lente sans raison pendant des heures.)

# Les droits du rôle applicatif sur ce qui vient d'être créé.
sudo -u postgres psql -d HbaExpressDb -c "
GRANT USAGE ON SCHEMA public TO \"HbaExpressUser\";
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"HbaExpressUser\";"
```

---

## 6. Basculer

Un seul drapeau, et il vient **en dernier**.

```yaml
# inventories/prod/group_vars/marketplace/main.yml
use_external_database: true
```

```bash
cd deploy/ansible
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit marketplace --tags deploy --ask-vault-pass --check --diff
```

Le diff sur `.env.prod` doit montrer **exactement quatre choses** :

- `MARKETPLACE_CONNECTION` passe de `Host=postgres` à `Host=10.20.0.2`
- `DISPATCH_CONNECTION` passe de `Host=postgres-dispatch` à `Host=10.20.0.2`
- les trois `POSTGRES_*` disparaissent
- `COMPOSE_PROFILES` se vide

**S'il montre autre chose, ne poursuivez pas.**

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml \
  --limit marketplace --tags deploy --ask-vault-pass
```

---

## 7. Vérifier, et seulement ensuite nettoyer

```bash
ssh -p 8022 ubuntu@79.137.35.129
cd /opt/marketplace
C="deploy/docker-compose.prod.yml"; E="deploy/.env.prod"

# Les deux conteneurs postgres ne doivent PLUS apparaître.
sudo docker compose -f "$C" --env-file "$E" ps

# L'API répond, et sa sonde de disponibilité teste réellement la base.
curl -fsS https://api.hba-express.org/health/ready && echo "  ← prêt"

# Aucun conteneur en boucle.
sudo docker compose -f "$C" --env-file "$E" ps --format json \
  | grep -i "restarting\|exited" && echo "PROBLÈME" || echo "tout est stable"
```

**Un piège à connaître.** Les conteneurs `postgres` et `postgres-dispatch` portent `restart: always`. Vider `COMPOSE_PROFILES` les retire de la pile, mais **ne les arrête pas** : Docker les relance au démarrage. Il faut les arrêter explicitement, sinon une base vide continue de répondre au nom `postgres` sur le réseau interne — exactement le scénario contre lequel tout ce document met en garde.

```bash
sudo docker stop marketplace-prod-postgres-1 marketplace-prod-postgres-dispatch-1
sudo docker update --restart=no marketplace-prod-postgres-1 marketplace-prod-postgres-dispatch-1
```

Puis les contrôles métier, les seuls à prouver que ce sont bien **vos** données : ouvrez la console d'administration, vérifiez qu'une commande connue est là, qu'un solde vendeur est le bon, qu'une recherche produit renvoie des résultats.

**Ne supprimez rien avant vingt-quatre heures.** Les volumes du VPS applicatif sont votre retour arrière ; ils ne coûtent que de l'espace disque.

```bash
# Après vingt-quatre heures de fonctionnement normal, et pas avant :
sudo docker rm marketplace-prod-postgres-1 marketplace-prod-postgres-dispatch-1
sudo docker volume rm marketplace_pgdata marketplace_pgdata-dispatch
```

---

## Le retour arrière

**Jusqu'à l'étape 6 incluse**, il tient en trois lignes : remettre `use_external_database: false`, relancer `--tags deploy`, et redémarrer les conteneurs locaux. Leurs volumes n'ont pas bougé — les données y sont exactement comme à l'étape 4.

```yaml
use_external_database: false
```

```bash
ansible-playbook -i inventories/prod/hosts.ini site.yml --limit marketplace --tags deploy --ask-vault-pass
```

La seule chose perdue est ce qui aurait été écrit sur le VPS base entre-temps. D'où l'intérêt de faire les contrôles de l'étape 7 **avant** de rouvrir le service aux clients.

**Après suppression des volumes**, le retour arrière passe par la sauvegarde — c'est-à-dire par la chaîne que l'étape 3 a précisément servi à valider.

---

## Ce que cette bascule ne règle pas

Pour mémoire, et pour ne pas croire le chantier terminé :

- **Le VPS base a divergé de sa description Ansible** sur cinq points, dont un port 5432 ouvert sur Internet — fermé le 14/09/2026. Après la bascule, repasser le play complet et comparer.
- **`postgresql.auto.conf` masque la configuration gérée par Ansible** depuis la fermeture d'urgence du port. À réinitialiser une fois `conf.d/10-marketplace.conf` vérifié.
- **L'archivage WAL reste désactivé.** Pas de restauration à un instant précis : le point de reprise est le dernier dump, six heures dans le pire cas.
- **Aucune règle d'alerte Postgres** dans `prometheus-rules.yml`, et le canal d'alerte reste vide. La surveillance de sauvegarde de l'étape 3 ne tient que par l'e-mail de cron.
- **L'authentification par mot de passe SSH** est encore active sur le VPS base.
