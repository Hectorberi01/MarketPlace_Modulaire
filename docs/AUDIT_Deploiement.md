# Audit de la structure de déploiement

**Date** : 9 septembre 2026
**Périmètre** : `deploy/` en entier — compose des trois environnements, Dockerfile, Traefik, Ansible, secrets, persistance, supervision, chaîne de livraison. **Cible prioritaire : la production.**
**Méthode** : cinq segments menés en parallèle, puis recoupement. Aucune commande Docker, aucune connexion au serveur : audit par lecture. **Les constats classés BLOQUANT ont tous été revérifiés à la main** ; ils sont marqués `[revérifié]`.
**Base** : l'arbre de travail courant, plus l'historique git complet.

---

## Ce que l'audit dit en une phrase

Le code applicatif est tenu avec soin ; son déploiement ne l'est pas. La production tourne aujourd'hui **sans sauvegarde, sans alerte qui atteigne un humain, avec trois surfaces d'administration ouvertes sur Internet, et sur des secrets qui ont été publiés en clair sur GitHub**. Aucun de ces quatre points n'est un défaut de conception : ce sont quatre choses qui ont été écrites, prévues, commentées — et jamais branchées.

C'est le motif central de cet audit, et il se répète partout : **le mécanisme existe, la ligne qui l'active manque.** Le script de sauvegarde est écrit et n'est planifié nulle part. Le rôle de sécurité existe et n'est pas activé en prod. Le canal d'alerte est câblé en staging et pas en production. Le garde-fou de test couvre cinq projets sur six. À chaque fois, la documentation décrit le mécanisme comme opérationnel.

---

## 0. Les quatre choses à traiter avant tout le reste

Elles sont indépendantes les unes des autres et toutes actives aujourd'hui.

| # | Fait | Coût de la correction |
|---|---|---|
| **A** | Les secrets de production ont été poussés en clair sur GitHub, dont la clé FedaPay **live** | Rotation (heures) puis réécriture d'historique |
| **B** | Il n'existe aucune sauvegarde de la base de production | Un rôle Ansible + une crontab (une heure) |
| **C** | Grafana et Portainer sont ouverts sur Internet sans authentification | Trois lignes, ce soir |
| **D** | Aucune alerte de production n'atteint qui que ce soit | Dix lignes de `main.yml` |

---

## 1. Les secrets

### 1.1 Le vault de production a été commité en clair, et poussé `[revérifié]`

`deploy/ansible/inventories/prod/group_vars/marketplace/vault.yml`, commit **`e4e9bcb`** (19/07/2026), commence par `---` et non par `$ANSIBLE_VAULT;1.1;AES256`. Quinze clés en clair :

```
admin_password          fedapay_api_key = sk_live_…     postgres_password
cloudinary_api_key      fedapay_webhook_secret          redis_password
cloudinary_api_secret   grafana_password                registry_token  (PAT GitHub)
cloudinary_cloud_name   jwt_signing_key                 resend_api_key
fcm_service_account_json (le compte de service Firebase entier)
```

Le vault **staging** est dans le même cas — commit `3df0158`, 20/07/2026 — avec en plus le mot de passe Neon et les clés R2.

Ces deux commits sont sur **quatre branches distantes** : `origin/develop`, `origin/develop_staging`, `origin/version_2.0.0`, `origin/wallet`.

Le fichier est chiffré aujourd'hui (`HEAD` commence bien par `$ANSIBLE_VAULT`). **Cela ne change rien** : `git log -p` ressort les blobs en une commande, et GitHub conserve les objets orphelins jusqu'à purge par son support.

**Conséquence.** `jwt_signing_key` permet de forger un jeton `Admin` — contrôle total de la plateforme. `fedapay_api_key` en `sk_live_` déplace de l'argent réel. `registry_token` est un accès en écriture au registre d'images, donc au code déployé.

**Ordre de traitement.** Roter d'abord, réécrire l'historique ensuite. L'inverse donnerait le sentiment que c'est réglé alors que les valeurs restent valides.

### 1.2 Le même secret Cloudinary sert en dev, en staging et en production

`cloudinary_api_secret` est identique dans `deploy/.env` (poste de développement, sur disque), dans le vault staging et dans le vault prod. Du point de vue de Cloudinary, il n'y a pas trois environnements : il y en a un.

Correction la plus économique : ces variables sont **mortes en production** (§5.4) — les supprimer du vault prod plutôt que les roter.

### 1.3 Un seul jeton R2 pour le bucket KYB privé et le bucket images public

```yaml
# docker-compose.prod.yml
80:  Storage__CloudflareR2__SecretAccessKey:      "${R2_SECRET_ACCESS_KEY:?…}"   # documents KYB, privé
86:  Storage__CloudflareR2Media__SecretAccessKey: "${R2_SECRET_ACCESS_KEY:-}"    # images produit, public
```

Le partage est assumé (`deploy/.env:37-38` : « Réutilise les mêmes Access Key / Secret ci-dessus »). Mais le chemin le plus exposé — l'upload d'images produit, appelé sur chaque fiche — porte des identifiants qui ouvrent aussi le bucket des **pièces d'identité et documents d'entreprise des vendeurs**.

Correction : deux jetons Cloudflare distincts, chacun cadré à son seul bucket.

### 1.4 Un identifiant de service média en dur, avec un commentaire qui dit l'inverse

```csharp
// MediaServiceOptions.cs:13-14
/// <summary>Identifiant client (en-tête « x-client-id ») — secret, hors du dépôt.</summary>
public string ClientId { get; set; } = "4f22…";
```

C'est le **seul** secret en dur trouvé dans `src/`. Deux aggravations : `CatalogModuleInstaller` ne surcharge cette valeur que si la configuration en fournit une non vide — le défaut est donc irréductible ; et `Media__ClientId` n'est écrit ni par `env.j2` ni par le compose, donc la production tourne bien sur la valeur du dépôt.

Atténuation : R2 étant configuré en prod, c'est `CloudflareR2MediaStorage` qui est sélectionné. Le secret est publié, pas utilisé.

### 1.5 Le gabarit de vault ne permet pas de reconstruire la production

`inventories/prod/vault.example.yml` propose 17 clés. Les rôles et le compose en exigent **13 de plus**, dont `admin_session_secret` (exigée en `:?` par le compose), `grafana_password` (sans `default()` dans son template : échec Jinja), `registry_token`, et les six secrets des verrous `webterminal` et `dbadmin` — dont les rôles échouent explicitement s'ils manquent.

Un opérateur qui reconstruit la prod depuis ce gabarit — le geste que le fichier documente en ligne 3 — obtient une cascade d'échecs, et la tentation de « débloquer » en désactivant les verrous.

---

## 2. La surface publique

### 2.1 Grafana répond à tout le monde, sans mot de passe `[revérifié]`

```yaml
# monitoring/docker-compose.monitoring.yml
93:   GF_AUTH_ANONYMOUS_ENABLED=true
94:   GF_AUTH_ANONYMOUS_ORG_ROLE=Viewer
103:  routers.grafana.rule=Host(`grafana.hba-express.org`)
106:  routers.grafana.middlewares=edge-chain-embed@file
```

`edge-chain-embed` = en-têtes + rate-limit + gzip. J'ai lu la chaîne : **ni authentification, ni filtrage d'IP.**

Le rôle Viewer autorise `datasources:query`, donc **Loki**. Et Promtail y déverse :

```yaml
70:  /var/lib/docker/containers:/var/lib/docker/containers:ro   # tous les conteneurs
72:  /var/log:/var/log/host:ro                                  # l'hôte, SSH compris
```

Un anonyme lit les journaux applicatifs — jetons, adresses, traces d'erreur — et les tentatives de connexion SSH au VPS.

Le fichier se met lui-même en garde deux lignes plus haut (`:91-92` : « restreignez l'accès au réseau interne / VPN via Traefik ») ; l'avertissement n'a pas été suivi. La cause est légitime : l'anonymat existe pour permettre l'iframe kiosk dans la console admin. La solution ne l'est pas.

**Correction durable** : couper l'anonymat et faire relayer les tableaux de bord par le BFF admin avec un jeton de service côté serveur. **Correction immédiate** : `GF_AUTH_ANONYMOUS_ENABLED=false`.

### 2.2 Portainer aussi — et Portainer donne root `[revérifié]`

```yaml
# portainer/docker-compose.yml
13:  image: ${PORTAINER_IMAGE:-portainer/portainer-ce:latest}
19:  /var/run/docker.sock:/var/run/docker.sock:ro
27:  routers.portainer.rule=Host(`portainer.hba-express.org`)
30:  routers.portainer.middlewares=edge-chain@file
```

Même chaîne, même absence de garde. Le `:ro` porte sur le nœud de fichier, **pas sur l'API Docker** : elle reste en écriture. Portainer compromis = création d'un conteneur privilégié = root sur l'hôte.

L'incohérence est frappante : le terminal web et pgweb — moins dangereux — ont chacun une allowlist IP **et** une authentification HTTP. Portainer, qui donne root, n'a rien. Et l'image est en `:latest` alors que son propre rôle dit de l'épingler.

### 2.3 Le terminal web ouvre un shell **root**

Les protections existent et sont réelles : `terminal-guard` = allowlist IP → auth HTTP → en-têtes, middleware bien attaché au routeur, et le rôle Ansible échoue si les listes sont vides. Mais :

```yaml
# inventories/prod/group_vars/marketplace/main.yml:81-85
# Le VPS OVH n'autorise pas root : wetty se connecte en "ubuntu" (shell sudo-capable)
terminal_ssh_user: "root"
terminal_ssh_port: "22"      # le commentaire dit 8022
```

Le commentaire décrit une configuration qui n'est pas celle du fichier. Franchir un mot de passe HTTP depuis une IP autorisée donne un shell root immédiat.

Aggravations : la clé publique est posée dans `/root/.ssh/authorized_keys` **sans aucune option de restriction** (`from=`, `restrict`, `command=`) — quiconque obtient la clé privée, en clair sur le disque de l'hôte, a root en SSH depuis n'importe où, sans passer par Traefik ; `X-Frame-Options` est supprimé sans `frame-ancestors` en remplacement (clickjacking sur un shell root) ; et `terminal-guard` ne contient **pas** `rate-limit`, donc le bourrage du mot de passe HTTP est libre.

### 2.4 Trois ports publiés sur `0.0.0.0` sans authentification

| Port | Service | Ce que ça donne |
|---|---|---|
| 4317 / 4318 | OTLP du collector | Injection de traces et métriques arbitraires : pollution des tableaux de bord, déclenchement ou **masquage** d'alertes, saturation disque |
| 3001 | Uptime Kuma, **en HTTP clair** | Identifiants en clair sur le réseau ; sur une instance neuve, le premier arrivant crée le compte admin ; SSRF vers le réseau Docker interne |

Le port 4317 ne sert à rien : les applications joignent `otel-collector:4317` par le réseau Docker. Le supprimer est plus propre que le restreindre.

### 2.5 Fail2ban et le pare-feu ne tournent pas en production `[revérifié]`

```yaml
# site.yml:25
when: enable_security | default(false)
```

`enable_security` n'est défini **que** dans l'inventaire staging. Absent de prod → le rôle est sauté : ni Fail2ban, ni rsyslog, ni UFW.

Et pourtant, dans l'inventaire prod :

```yaml
106: fail2ban_findtime: "1h"
107: fail2ban_bantime: "24h"
108: fail2ban_sshd_maxretry: 3
```

Trois réglages fins, avec un commentaire décrivant un incident réel (« 0 ban malgré 114 échecs SSH constatés »). **Ils ne sont lus par personne.** On croit avoir corrigé le brute-force lent ; il n'y a pas de Fail2ban du tout. Trois alertes de supervision (`SshBruteForce`, `RootLogin`, `Fail2banBanSpike`) reposent sur des journaux que ce rôle est seul à produire : elles sont inertes.

Nuance honnête : même activé, UFW **ne rattraperait pas** les ports du §2.4. Docker insère ses règles DNAT en amont de celles d'UFW. La bonne correction reste le binding sur `127.0.0.1`.

### 2.6 Ce qui est correctement fermé

À protéger en corrigeant le reste. `exposedByDefault: false` sur le provider Docker : rien n'est publié sans label explicite. Ni Postgres ni Redis ne sont exposés en prod. Le tableau de bord Traefik et son API sont désactivés, l'entrypoint `metrics` n'est pas publié. La redirection HTTP→HTTPS est globale et permanente, sans casser le challenge ACME (`tlsChallenge` sur 443). HSTS avec `includeSubdomains` et `preload`. Swagger est conditionné à `IsDevelopment()` sur les six hôtes. Le routeur de l'API ne publie que les rappels PSP, celui du dispatch que `/api/courier` et `/hubs/courier` — `/internal` n'est joignable par aucune règle. Le limiteur de débit applicatif partitionne sur la **vraie** IP client, `UseProxyForwardedHeaders` étant le premier middleware du pipeline : c'est le piège classique, il est évité.

### 2.7 Deux réserves de second ordre

**Le préfixe de chemin est contournable par normalisation.** Traefik filtre sur le chemin **tel que reçu** ; ASP.NET normalise `..` avant routage. Une requête vers `/api/courier/../internal/…` matche la règle Traefik et atteint `/internal` côté applicatif. Ce qui rattrape : la `FallbackPolicy` sur les six hôtes et la signature HMAC horodatée, fermée par défaut hors développement. On perd la première ligne de défense, pas l'intégrité. Correction : `PathRegexp(^/api/courier(/|$))`.

**La surface entière des quatre BFF est publiée** par une règle `Host()` sans filtre de chemin, alors que le travail de réduction a été fait sur `api` et `dispatch`. Le contrôle applicatif couvre, mais `/health/ready` reste un oracle de disponibilité anonyme — et le commentaire du code affirme le contraire.

---

## 3. Les données

### 3.1 Il n'existe aucune sauvegarde de la base de production `[revérifié]`

`deploy/postgres/backup.sh` existe et il est correct. Il n'est installé, appelé ni planifié par **rien** :

- `deploy/ansible/roles/` contient `docker`, `security`, `portainer`, `marketplace`, `webterminal`, `dbadmin`, `monitoring`. **Pas de rôle `backup`.**
- Aucune tâche cron, aucun timer systemd, nulle part.
- `deploy-prod.sh` a cinq étapes ; aucune n'est une sauvegarde.

Les deux seules traces sont des cases à cocher **vides** dans `ENV-PROD.md` (« Sauvegardes DB planifiées », listée parmi les « bloqueurs connus ») et un commentaire dans le script lui-même : « À planifier via cron sur l'hôte ».

**Conséquence.** Une plateforme qui tient des paiements, des portefeuilles vendeurs, des séquestres et des retraits n'a aucune copie de sa base. Perte du VPS, `DROP` accidentel, corruption disque, migration ratée : tout est définitivement perdu.

### 3.2 Et même planifié, le script effacerait ses propres dumps `[revérifié]`

```bash
# postgres/backup.sh:26,29
APP_DIR="${APP_DIR:-/opt/marketplace}"
BACKUP_DIR="${BACKUP_DIR:-$APP_DIR/backups}"
```

Or le rôle `marketplace` synchronise le dépôt vers ce même répertoire avec `delete: true`, et sa liste d'exclusions ne contient pas `backups`. Chaque `ansible-playbook site.yml` supprimerait donc `/opt/marketplace/backups/` — c'est-à-dire au moment précis, le déploiement, où l'on a le plus besoin d'une copie.

Correction : `BACKUP_DIR=/var/backups/marketplace`, hors de `app_dir`, plus un `--exclude=backups` par ceinture.

### 3.3 La documentation croit encore à un Postgres managé avec PITR

`docs/11-production-architecture.md` décrit la persistance comme « PostgreSQL | Neon (PITR) ». La réalité est un conteneur `postgres:16-alpine` sur le VPS, avec un volume Docker local. `postgresql.conf` déclare `wal_level = replica` mais **aucun** `archive_mode` ni `archive_command` : les 4 Go de WAL sont recyclés en place, ils n'offrent aucune capacité de restauration.

Quiconque lit la doc conclut que les sauvegardes sont couvertes par le fournisseur. Elles ne le sont par personne.

### 3.4 Aucune procédure de restauration, donc aucune testée

La seule trace est un exemple en commentaire, sur deux lignes. Ni `README.md` ni `CICD.md` ne mentionnent la restauration. Deux problèmes dans l'exemple proposé : le dump est fait `--no-owner --no-privileges`, donc les droits sont à rejouer à la main sur un cluster où l'utilisateur applicatif diffère du superutilisateur ; et `psql` sans `ON_ERROR_STOP` laisse une base **partiellement peuplée** sans le signaler.

Le dépôt le sait pourtant : `docs/11-production-architecture.md` écrit « une sauvegarde jamais restaurée n'est pas une sauvegarde », suivi d'une case à cocher vide.

### 3.5 Les migrations, appliquées au démarrage, sans verrou et sans retour

**26 `DbContext`** sont migrés séquentiellement au démarrage de l'API, avant que Kestrel n'ouvre son port. Point positif : chaque module a son schéma et sa propre table d'historique — pas de collision.

Trois défauts :

**Aucun verrou.** Le projet est en EF Core 8 ; le verrou de migration n'existe qu'à partir d'EF Core 9. Aujourd'hui l'API tourne en instance unique, donc le risque est latent — il devient réel au premier `--scale api=2`, au premier déploiement en rolling, ou lors d'un redémarrage rapide où l'ancien conteneur n'est pas encore mort.

**Un échec met toute la pile en boucle.** `MigrateAsync` n'est pas gardé : l'exception sort du processus, `restart: always` relance, la migration rejoue et échoue au même endroit. Or les quatre BFF **et** le dispatch attendent `api: service_healthy`. Une seule migration en échec — ou simplement longue, un `CREATE INDEX` non concurrent — et plus rien ne démarre. Panne totale, déclenchée par un déploiement.

**Le retour arrière est impossible.** Une migration isolée qui échoue est annulée, mais **la séquence ne l'est pas** : si le 17e contexte échoue, les 16 précédents sont commités. Le schéma est dans un état qu'aucune version du code ne connaît — et il n'y a pas de sauvegarde à restaurer.

### 3.6 Postgres : deux réglages à corriger avant la mise en service

**L'encodage et la collation ne sont pas spécifiés.** Aucun `POSTGRES_INITDB_ARGS`, aucun `LANG` nulle part. `initdb` s'exécute donc avec la locale par défaut de l'image alpine, c'est-à-dire `C`. Le tri se fait en ordre d'octets : `Émile` passe après `Zoé`, et `é ≠ e` pour tout `ORDER BY` de nom de produit ou de vendeur — sur une marketplace francophone. `LC_COLLATE` est **figé à `initdb`** et ne se change que par dump/restore complet. Chaque jour d'exploitation renchérit la correction.

**`max_connections = 200` contre ~75 connexions réelles.** Le pool Npgsql vaut 15 par défaut, sur cinq hôtes. Les 125 connexions restantes sont réservées en mémoire ; à `work_mem = 10MB` par nœud de tri, le pire cas dépasse la limite de 8 Go du conteneur. Un OOM-kill de Postgres pendant une écriture est un scénario de corruption — sans sauvegarde.

S'y ajoutent `effective_cache_size = 12GB` sur un conteneur plafonné à 8 Go (le planificateur suppose un cache qui n'existe pas) et `datestyle = 'iso, mdy'` sur une plateforme francophone.

### 3.7 Redis : persistance non voulue, et l'alerte qui devait rattraper est neutralisée

Ce Redis ne contient que deux choses — le cache distribué et le backplane SignalR — et **rien qui ne soit reconstructible**. Vérifié : pas de sessions (cookie chiffré), pas de verrous, pas de clés d'idempotence (portées en base), pas de compteurs de rate limiting.

Or `--appendonly yes` fait fsync à chaque écriture de cache, sur le même disque que `pgdata`, sans `maxmemory` ni politique d'éviction. L'AOF croît sans borne et peut remplir le disque — ce qui **arrête Postgres**. Un cache reconstructible devient la cause d'une panne de la base, qui ne l'est pas.

Et l'alerte `RedisHighMemory` est conditionnée par `redis_memory_max_bytes > 0`. Sans `maxmemory`, cette valeur vaut 0 : **l'alerte ne se déclenche jamais**. La supervision est neutralisée par le problème même qu'elle devait signaler.

Correction : `--appendonly no --save "" --maxmemory 512mb --maxmemory-policy allkeys-lru`.

### 3.8 Un dump de la base est versionné

`deploy/marketplace_avant_xof.dump`, 189 Ko, suivi par git, produit juste avant la conversion EUR→XOF. S'il vient de la base réelle, des données clients sont dans l'historique, distribuées à tout porteur du dépôt.

---

## 4. La chaîne de déploiement

### 4.1 Aucune alerte de production n'atteint qui que ce soit `[revérifié]`

Le gabarit d'Alertmanager n'émet un receiver Slack que si `alertmanager_slack_webhook_url` est défini et non vide, et un receiver e-mail que si `smtp_host` **et** `alert_email_to` le sont. Ces variables sont définies **uniquement en staging** :

```yaml
# inventories/staging/…/main.yml
116: alert_slack_channel: "#alerts"
117: smtp_host: "smtp.hostinger.com"
121: alert_email_to: "hector.adjakpa@gmail.com"
```

Dans `inventories/prod/…/main.yml` : **rien**. `smtp_host` et `alert_email_to` ne sont pas des secrets — ils vivent en clair en staging — donc leur absence en prod est définitive : la voie e-mail est morte.

Alertmanager accepte une configuration à receivers vides et **abandonne silencieusement** chaque notification. `OutboxDeadLetter` — dont le commentaire dit « UN SEUL suffit à réveiller quelqu'un » — et `DispatchOutboundDeadLetter` — « LA RÉMUNÉRATION DU COURSIER N'A PAS ÉTÉ VERSÉE » — se déclenchent dans une interface web que personne ne regarde.

Une alerte qui ne réveille personne n'existe pas. La production n'a, aujourd'hui, aucune alerte.

### 4.2 `ApiDown` ne se déclenche pas quand l'API est morte

```yaml
expr: api_up == 0
```

`api_up` est émise par l'application elle-même. Si le conteneur s'arrête, plante ou boucle, il n'émet plus rien : la série **disparaît** au lieu de valoir 0, et une expression `métrique == 0` sur une série absente ne renvoie aucun échantillon.

L'alerte couvre donc « l'API se déclare malade » — le cas bénin — et manque « l'API n'est plus là ». Correction : `api_up == 0 or absent(api_up)`, et une alerte sur `up{job="otel-collector"} == 0`, sans quoi **toutes** les alertes applicatives deviennent muettes sans que rien ne le signale.

### 4.3 Les alertes qui manquent

| Manque | Ce qu'on ne saura pas |
|---|---|
| **Sauvegarde** | Qu'il n'y en a pas. L'alerte qui l'aurait révélé n'existe pas non plus |
| **Postgres** | `postgres-exporter` est déployé et scrapé, et il n'existe **aucune** règle : ni `pg_up`, ni connexions saturées, ni transaction bloquante. La base tient l'argent |
| **Certificats expirants** | Un renouvellement ACME cassé se découvre le jour où les visiteurs voient un avertissement de sécurité |
| **Watchdog** | Que Prometheus, Alertmanager ou le canal sont morts — c'est-à-dire exactement la panne du §4.1 |
| **Retard de l'outbox** | Un processeur arrêté ne produit ni lettre morte ni échec : la file grossit en silence et les alertes existantes restent à zéro |
| **Conteneurs en boucle** | Complète le §4.6 : ni le déploiement ni la supervision ne voient un service qui redémarre indéfiniment |

### 4.4 Le script teste un code et déploie un autre

`deploy-prod.sh` se présente comme un « VERROU » qui valide la compilation et les tests avant de toucher au serveur. Il teste l'arbre de travail local. Mais l'inventaire prod est en `use_registry: true` / `image_tag: latest`, et le rôle se contente de `pull` puis `up -d --no-build`.

Aucune ligne du script ne construit ni ne pousse d'image. **Ce qui part en production est la dernière image poussée par la CI** — un commit potentiellement différent. Les tests verts ne disent rien sur le binaire déployé, et un opérateur qui corrige un bug localement et lance le script ne déploie rien de sa correction.

### 4.5 `latest` partout : ni traçabilité, ni retour arrière

Les huit images sont en `${IMAGE_TAG:-latest}`, et l'inventaire prod fixe `image_tag: latest`. Aucun tag `:<sha>` n'est poussé pour le backend.

Deux conséquences distinctes. **Aucun retour arrière** : `latest` a déjà été écrasé, le SHA précédent n'existe pas sur le registre, la seule sortie d'un incident est de reconstruire depuis git — sur une CI qui est peut-être en cause. **Le parc n'est pas homogène** : `up -d --no-build` ne recrée un conteneur que si le digest local a changé ; deux services redéployés à dix minutes d'intervalle peuvent tourner deux builds différents sans que `docker compose ps` le montre.

Le `README.md` décrit pourtant précisément la bonne procédure — « chaque image porte deux étiquettes, `:latest` mobile et `:<sha>` fixe, la seconde permet de revenir en arrière » — mais ce paragraphe concerne la console vendeur, dont les images viennent d'un autre dépôt. Appliqué au backend, il désigne des images qui n'ont jamais été poussées.

À noter, deux services montrent que l'équipe sait faire : `rembg` est épinglé à une version précise, avec en commentaire la CVE que le pin protège.

### 4.6 Rien ne vérifie qu'un déploiement a réussi

La dernière chose que fait le rôle est d'**afficher** `docker compose ps`. Aucun `assert`, aucun appel sur `/health`, aucun `wait_for`. Toutes les commandes portent `changed_when: true`, donc Ansible signale « changed » quoi qu'il arrive.

**Un conteneur en boucle de redémarrage produit un déploiement vert**, en CI comme en manuel. Le dépôt sait faire mieux ailleurs : le workflow de la console vendeur « attend que le conteneur soit `healthy` et échoue sinon ».

### 4.7 Le service de dispatch n'est jamais testé par la CI

Le job de garde-fou ne connaît qu'une solution, `Marketplace.sln`. Or `build-and-push.sh` construit et pousse une **sixième** image à partir de `Services/HbaExpress.Dispatch`.

Le script local, lui, le sait, et le dit en toutes lettres : « Sans cette ligne, le verrou de compilation et les tests ne couvriraient pas le service qui tient les courses et déclenche le paiement des coursiers — et une régression y partirait en production sans être vue. » C'est exactement ce qui se passe, par le chemin qui déploie réellement.

### 4.8 Les tests se désactivent par une variable d'environnement

`--skip-tests` est un mot sur la ligne de commande, avec un avertissement affiché après coup. Bien pire : `DOTNET_TEST_ARGS` est injecté sans quotes dans la commande de test. Un `export DOTNET_TEST_ARGS='--filter FullyQualifiedName=RienDuTout'` fait passer la suite à zéro test exécuté, avec un « Tests OK » vert — et la variable peut vivre dans le `.zshrc` de l'opérateur.

Correction : journaliser le nombre de tests exécutés et échouer s'il est nul.

### 4.9 Postgres peut être redémarré pendant que l'API migre

Le rôle vérifie `shared_preload_libraries` avec `failed_when: false`, puis redémarre Postgres si la réponse ne contient pas `pg_stat_statements`, et **ensuite seulement** attend `pg_isready`. L'attente est après le test, pas avant.

Si le `psql` tombe pendant que Postgres finit de démarrer, la sortie est vide, la condition est vraie, et Postgres redémarre — sous les pieds d'une API qui applique ses 26 migrations. Le redémarrage est en outre inutile : `postgresql.conf` contient déjà cette extension.

### 4.10 Le déploiement n'est pas atomique

Trois fenêtres : `up -d` sans `--wait` recrée tout simultanément, et les BFF attendent que l'API ait migré et repassé `healthy` — plusieurs dizaines de secondes de 502 sur toute la surface publique ; `--force-recreate traefik` est exécuté **à chaque déploiement** sans condition, coupant toutes les connexions y compris celles des services inchangés ; et le rôle monitoring redémarre inconditionnellement six conteneurs, ce qui remet à zéro les clauses `for:` de Prometheus — les alertes en cours d'incubation sont perdues au moment où le risque est le plus élevé.

### 4.11 Le déploiement dépend de l'état du poste de l'opérateur

Cinq dépendances non épinglées : le SDK .NET (`global.json` en `rollForward: latestMajor` + `allowPrerelease`), la version d'Ansible (`pip install --upgrade`), les collections Galaxy (contraintes `>=`), la clé SSH (implicite, via l'agent), et surtout **le contenu déployé est l'arbre de travail, pas un commit** — le script avertit que l'arbre n'est pas propre et n'en tire aucune conséquence.

Deux opérateurs, deux résultats. Un fichier oublié dans l'arbre local part en production sans revue, et rien dans l'historique git ne dira ce qui tourne.

### 4.12 Les deux chemins de déploiement divergent

La CI applique huit tags de rôles ; le script manuel n'en applique qu'un (`deploy`). Un déploiement manuel ne régénère donc jamais la configuration d'Alertmanager ni les middlewares Traefik du terminal et de pgweb. L'état du serveur dépend de qui a déployé en dernier, et comment.

---

## 5. Le compose et la cohérence des environnements

### 5.1 La pile de production ne peut pas démarrer par le chemin documenté

Quatre variables sont exigées en `:?` par le compose prod — `DISPATCH_CONNECTION`, `DISPATCH_OUTBOUND_SECRET`, `DELIVERY_WEBHOOK_SECRET`, `DISPATCH_COURIER_SHARE` — et **aucune n'est produite** par `env.j2`, le seul générateur du fichier d'environnement. Elles n'existent que dans le gabarit de **développement**.

Compose interpole tout le fichier avant de créer quoi que ce soit : le `pull` s'arrête sur « required variable is missing », et rien n'est mis à jour. Soit la prod tourne sur un `.env.prod` édité à la main hors Ansible — que le prochain déploiement écrasera — soit le dispatch n'a jamais été déployé.

Sept autres variables sont dans le même cas mais avec un défaut `:-` : elles se contentent d'être vides, ce qui est plus insidieux.

### 5.2 `env.j2` transforme « absente » en « présente mais vide »

Le gabarit écrit systématiquement la ligne, vide si la clé de vault manque. Or `${X:?}` échoue sur une valeur vide exactement comme sur une valeur absente — mais le message dira « variable requise manquante » alors que l'opérateur l'a bien renseignée, sous un autre nom, dans le vault.

Le bon patron existe déjà dans ce même fichier, appliqué à un seul cas, avec un commentaire qui explique précisément pourquoi. Il suffit de le généraliser.

### 5.3 Le dispatch n'a pas de base en production

Le compose de dev déclare un moteur `postgres-dispatch` séparé. Prod et staging n'en ont **aucun**, et le `depends_on` du dispatch ne mentionne aucune base — alors que ce service porte `RunMigrationsAtStartup: "true"`.

Le commentaire du compose interdit d'utiliser la base marketplace (« ce service réécrit des positions à la seconde, la marketplace tient de l'argent »), mais c'est le seul Postgres disponible. Ni base externe ni seconde base logique ne sont documentées côté prod.

Ordre de démarrage : la dépendance est aujourd'hui satisfaite **transitivement** (dispatch → api → postgres), donc par accident. Elle casse le jour où l'API cesse de dépendre du conteneur Postgres.

### 5.4 Les vraies dérives entre staging et prod

Sur 392 lignes divergentes, la plupart sont légitimes — domaines, gabarits, ACME, durcissement des `:?`. Quatre sont de vraies dérives :

- **`Cors__Origins` manque au BFF vendeur en prod.** Staging l'a, les trois autres BFF de prod l'ont. Ce n'est pas une simplification : la variable continue d'être générée et documentée. Quelqu'un a retiré la ligne du compose, pas de la chaîne. Le repli n'est pas fermé pour ce BFF : `appsettings.json` embarque deux origines `localhost` que le code autorise **avec `AllowCredentials()`**.
- **Le healthcheck de la console, corrigé en prod seulement.** Staging est resté sur `curl` là où l'image Node n'en a pas. Le staging ne peut donc plus valider ce que la prod fait.
- **`Monitoring__PortainerUrl` absent en prod.** Portainer tourne, son lien ne s'affiche jamais dans la console admin — et son absence ne produit aucune erreur.
- **`Identity__Bootstrap__*` dans les cinq hôtes en staging**, dans le seul `api` en prod. Le mot de passe admin est dans l'environnement de quatre processus qui n'en ont pas l'usage.

### 5.5 Le dev n'a pas les mêmes services que la prod

Ni `rembg`, ni `seller-console`, ni `console`. Trois coûts concrets : le détourage d'image n'est jamais exercé localement (l'adaptateur nul est sélectionné) ; la couche console — session par cookie chiffré, relais BFF — n'est jamais exercée, ce qui explique qu'un healthcheck cassé ait survécu si longtemps en staging ; et le parcours d'inscription complet, avec validation en console, ne peut pas être joué sans déployer.

### 5.6 Les conteneurs applicatifs tournent en root

Le Dockerfile ne contient aucune directive `USER`. Les six conteneurs .NET tournent donc en uid 0. Combiné au fait que Traefik monte `/var/run/docker.sock`, une évasion depuis l'un de ces six processus démarre root.

Correction : `USER $APP_UID` avant l'`ENTRYPOINT` — l'image aspnet 8 définit déjà cette variable.

### 5.7 Les secrets entrent dans le contexte de build

Le Dockerfile fait `COPY . .` et `.dockerignore` n'exclut qu'un chemin exact : `deploy/.env`. `deploy/.env.prod` — le nom qu'Ansible écrit sur le serveur — n'est pas couvert, ni `deploy/fcm/service-account.json`, ni le dump de 189 Ko.

Atténuation : le chemin Ansible en prod saute l'étape de build. Le risque est sur le chemin **manuel que le compose documente lui-même en ligne 17**. Ces secrets restent dans l'étage `build`, donc rien n'est poussé au registre — mais la couche persiste dans le cache local de la machine de build.

Correction : `deploy/.env*`, `deploy/fcm/`, `*.dump` dans `.dockerignore`. Une ligne referme les trois cas.

### 5.8 Le healthcheck ne teste rien

```yaml
test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
```

`curl` est bien présent dans l'image. Mais `/health` est monté avec `Predicate = _ => false` : il n'exécute **aucun** check et répond 200 dès que Kestrel écoute. `/health/ready`, qui teste Postgres et Redis, n'est utilisé par aucun healthcheck du compose.

Conséquence : un conteneur qui a perdu Postgres ou Redis reste `healthy`, Docker ne le redémarre pas, Traefik continue de lui router du trafic.

Point connexe : `start_period: 40s` + 10 tentatives à 15 s ≈ 190 s. Sur une base neuve, l'API migre 26 schémas avant d'ouvrir son port. Si ce premier démarrage dépasse 190 s, l'API est déclarée malsaine, relancée, et la migration recommence — boucle.

### 5.9 Le budget mémoire de l'hôte n'est pas bouclé

13,8 Go de plafonds déclarés, plus quatre conteneurs sans aucun plafond : `redis`, `traefik`, `console`, `seller-console`. Avec Postgres autorisé à 8 Go juste à côté, la victime d'un OOM n'est pas décidable à l'avance — et si c'est Postgres, `restart: always` rejoue le démarrage sur un répertoire de données tué en écriture.

### 5.10 L'en-tête du compose de production décrit une autre architecture

Les neuf premières lignes du fichier le plus sensible du dépôt annoncent « base de données EXTERNE (Neon/Postgres managé) », « Pas de Postgres local », et une route `web.$ROOT_DOMAIN`. Le fichier définit un service `postgres` local, l'API en dépend, et aucun service ne sert `web.`.

Trois affirmations fausses, que le prochain opérateur d'astreinte lira à trois heures du matin.

---

## 6. Ce qui est solide

À protéger en corrigeant le reste.

**`JwtKeyGuard` est armé sur les cinq hôtes** : refus de démarrer en Production si la clé est celle de développement ou fait moins de 32 caractères. Les cinq `appsettings.json` portent bien la clé de dev en défaut — sans ce garde-fou, un oubli de variable aurait donné une plateforme dont n'importe qui forge un jeton `Admin`.

**Les webhooks PSP refusent de démarrer sans secret** en Production. Sans ce contrôle, une passerelle configurée sans secret accepterait toute notification de paiement non signée.

**La signature partenaire du canal coursier** est fermée par défaut : HMAC-SHA256 sur `horodatage.corps`, comparaison en temps constant, fenêtre de cinq minutes, mode permissif réservé au développement.

**Le cache tolère la panne.** Lectures et écritures Redis sont enveloppées avec repli sur la base. Perdre Redis coûte un pic de charge, pas une panne.

**L'amorçage admin** est idempotent, sans mot de passe par défaut, et **relit la base** pour vérifier que le compte peut réellement se connecter. Aucun compte de démonstration n'est semé.

**Le cloisonnement des schémas** : 26 contextes, chacun son schéma et sa propre table d'historique de migration. Pas de collision possible entre modules.

**La rotation des journaux est universelle** : douze services sur douze, soit un plafond global d'environ 360 Mo. Aucun conteneur ne peut remplir le disque par ses logs.

**Le Dockerfile** est un vrai multi-étage : seul `/app` traverse, pas de SDK dans l'image finale, cache NuGet BuildKit avec `sharing=locked` et le commentaire qui explique la panne concurrente qu'il évite, et un `ENTRYPOINT` avec `exec` — PID 1 est `dotnet`, donc SIGTERM arrive au runtime et l'arrêt est propre. Détail souvent raté.

**Le triple verrou de pgweb** : rôle PostgreSQL en lecture seule, `--readonly --lock-session`, allowlist IP et authentification HTTP attachées au routeur, plus un garde-fou Ansible qui refuse l'activation sans secrets. C'est le modèle qu'il faut appliquer à Portainer.

**Le rate limiting applicatif** partitionne sur la vraie IP client : `UseProxyForwardedHeaders` est le premier middleware, avec `KnownNetworks` restreint aux plages privées. C'est le piège classique du reverse proxy, et il est évité.

---

## 7. Ordre de réparation proposé

D'abord ce qui est déjà exploité ou peut l'être aujourd'hui, ensuite ce qui empêche de se relever d'un incident, enfin ce qui ment.

### Ce soir

1. **Roter les secrets de `e4e9bcb`** — FedaPay live d'abord, puis le PAT GitHub et le compte Firebase, puis le reste. §1.1
2. **`GF_AUTH_ANONYMOUS_ENABLED=false`** et redémarrer Grafana. §2.1
3. **Dépublier Portainer** (commenter ses labels Traefik ; l'accès reste possible par tunnel SSH) le temps de lui poser un garde. §2.2
4. **Binder 4317, 4318 et 3001 sur `127.0.0.1`**, ou supprimer ces ports. §2.4

### Cette semaine

5. **La sauvegarde** : un rôle Ansible qui pose `backup.sh` et sa crontab, avec `BACKUP_DIR=/var/backups/marketplace`, plus le bucket R2 et sa règle de cycle de vie. §3.1, §3.2
6. **Restaurer une fois, pour de vrai**, sur une machine jetable. Une sauvegarde jamais restaurée n'est pas une sauvegarde — la phrase est déjà dans vos documents. §3.4
7. **Le canal d'alerte en prod** : recopier le bloc Alertmanager de staging dans l'inventaire prod, ajouter les deux secrets au vault et au gabarit, puis valider avec une alerte de test. §4.1
8. **`enable_security: true`** et `enable_ufw: true` en prod — vos réglages Fail2ban attendent d'être lus. §2.5
9. **Le terminal web** : passer à un compte non-root, restreindre la ligne `authorized_keys`, rétablir une protection contre l'encadrement, ajouter le rate-limit à sa chaîne. §2.3

### Ensuite, dans cet ordre

10. **Pousser `:<sha>` et déployer par SHA.** Cela résout d'un coup la traçabilité, le retour arrière et le lien entre les tests et le binaire. §4.4, §4.5
11. **Sauvegarde bloquante avant le `up`, vérification de santé après.** Les deux garde-fous qui manquent aux extrémités de chaque déploiement. §3.5, §4.6
12. **Les variables du dispatch dans `env.j2`**, et le patron `{% if … is defined %}` généralisé — sans quoi le déploiement documenté ne démarre pas. §5.1, §5.2
13. **Décider où vit la base du dispatch** en staging et en prod, et la câbler. §5.3
14. **Les alertes manquantes** : Postgres, âge de la dernière sauvegarde, certificats, watchdog, retard de l'outbox, conteneurs en boucle. Et corriger `ApiDown` en `== 0 or absent(...)`. §4.2, §4.3
15. **Le healthcheck sur `/health/ready`**, avec un `start_period` allongé sur l'API. §5.8
16. **`USER $APP_UID`** dans le Dockerfile, et `.dockerignore` complété. §5.6, §5.7
17. **Redis plafonné**, AOF coupé ; `effective_cache_size` aligné sur la limite du conteneur ; `max_connections` ramené à ~100. §3.6, §3.7
18. **La collation Postgres** — à trancher maintenant, car elle se fige à la création du cluster. §3.6
19. **Les quatre dérives staging/prod**, à commencer par le `Cors__Origins` du BFF vendeur. §5.4
20. **Les documents qui mentent** : l'en-tête du compose prod, `README.md` sur le rebuild et le retour arrière, `CICD.md` sur le nombre d'images, `docs/11-production-architecture.md` sur Neon. Les corriger coûte une heure et fait gagner des jours au prochain lecteur — ou une nuit à celui d'astreinte.

---

*Audit mené sans Docker ni accès au serveur. Les constats sont établis par lecture ; aucun n'a été reproduit à l'exécution. Les quatre plus graves ont été revérifiés à la main après les rapports initiaux.*
