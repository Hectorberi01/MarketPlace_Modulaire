# Architecture VPS de production

Ce document propose une architecture cible pour exécuter HbaExpress en production,
à partir de l'existant (staging mono-VPS 4 Go / 2 vCPU) et des risques identifiés.

---

## 1. Ce qu'il faut réellement héberger

| Bloc | Conteneurs | Nature |
| --- | --- | --- |
| **Edge** | `traefik` | TLS (ACME), routage, middlewares sécurité |
| **Applicatif** | `api`, `bff-web`, `bff-mobile`, `bff-seller`, `bff-admin` | 5 process .NET 8 |
| **Cache** | `redis` | Cache + sessions/refresh |
| **Observabilité (stockage/UI)** | `prometheus`, `loki`, `tempo`, `grafana`, `alertmanager`, `otel-collector`, `uptime-kuma` | Lourd (RAM + disque) |
| **Observabilité (agents)** | `promtail`, `cadvisor`, `node-exporter`, `redis-exporter` | Léger — doit tourner sur **chaque** hôte |
| **Ops** | `portainer` | Contrôle Docker = **root sur l'hôte** |

**Postgres n'est pas hébergé** : la base est **managée** (Neon). C'est un choix sain —
sauvegardes, PITR et bascules ne sont pas à votre charge. Cela change le dimensionnement :
le plus gros consommateur habituel de RAM et de disque est absent du VPS.

---

## 2. Architecture cible

```
                        Internet
                            │
                   ┌────────┴────────┐
                   │   DNS + WAF/CDN │  (Cloudflare, optionnel)
                   └────────┬────────┘
                            │ 443
      ╔═════════════════════▼═══════════════════════╗
      ║  HÔTE A — Application        4 vCPU / 8 Go  ║
      ║  ─────────────────────────────────────────  ║
      ║  traefik (80/443 publics)                   ║
      ║  api · bff-web · bff-mobile                 ║
      ║  bff-seller · bff-admin                     ║
      ║  redis                                      ║
      ║  agents : promtail, cadvisor, node-exporter ║
      ╚═════════════════════╤═══════════════════════╝
                            │ réseau PRIVÉ (pas d'Internet)
      ╔═════════════════════▼═══════════════════════╗
      ║  HÔTE B — Observabilité      2 vCPU / 8 Go  ║
      ║  ─────────────────────────────────────────  ║
      ║  otel-collector · prometheus · loki · tempo ║
      ║  grafana · alertmanager · uptime-kuma       ║
      ║  portainer                                  ║
      ║  aucun port public — accès via VPN/SSH      ║
      ╚═════════════════════════════════════════════╝

  Services managés (hors VPS) :
    • PostgreSQL — Neon  ⚠️ à co-localiser (voir §5)
    • Objets      — Cloudflare R2 (documents, médias)
    • Images      — Cloudinary
    • Paiements   — FedaPay      • Push — FCM
```

### Pourquoi deux hôtes et pas un seul

Ce n'est pas une coquetterie. Aujourd'hui, **aucune limite mémoire n'est déclarée** sur
les conteneurs : un pic de Loki (rattrapage de backlog) ou une explosion de cardinalité
dans Prometheus consomme jusqu'à l'OOM-killer du noyau, qui tue alors **un process
arbitraire** — potentiellement votre API. Isoler l'observabilité supprime cette classe
entière de pannes : au pire, vous perdez les dashboards, pas les ventes.

Second effet : l'hôte B n'a **aucun port public**. Grafana et Portainer — les deux
surfaces les plus sensibles — sortent d'Internet.

---

## 3. Dimensionnement

### Hôte A — Application : **4 vCPU / 8 Go / 80 Go NVMe**

| Conteneur | RAM au repos | Limite conseillée |
| --- | --- | --- |
| `api` | 200–350 Mo | `1g` |
| `bff-*` (×4) | 120–200 Mo chacun | `512m` chacun |
| `redis` | 50–150 Mo | `512m` (+ `maxmemory 256mb`) |
| `traefik` | 50–100 Mo | `256m` |
| agents (×3) | ~150 Mo au total | `256m` chacun |
| **Total plafonné** | | **≈ 4,5 Go** |

8 Go laissent donc ~3,5 Go de marge pour les pics de charge et le cache disque du noyau.
4 vCPU : les 5 process .NET sont I/O-bound (attente DB/HTTP) plus que CPU-bound ; 2 vCPU
suffisent au repos mais saturent dès qu'un GC concurrent croise un pic de trafic.

### Hôte B — Observabilité : **2 vCPU / 8 Go / 160 Go**

| Conteneur | Limite conseillée |
| --- | --- |
| `prometheus` | `2g` |
| `loki` | `1g` |
| `tempo` | `1g` |
| `grafana` | `512m` |
| `otel-collector` | `512m` |
| `alertmanager`, `uptime-kuma`, `portainer` | `256m` chacun |
| **Total plafonné** | **≈ 6 Go** |

Le disque est dicté par la **rétention**, pas par la RAM. Fixez-la explicitement :

| Donnée | Rétention conseillée | Ordre de grandeur |
| --- | --- | --- |
| Métriques (Prometheus) | 30 j | quelques Go |
| Logs (Loki) | 14 j | le plus volumineux (Traefik est bavard) |
| Traces (Tempo) | 7 j | modéré, à échantillonner si besoin |

Sans rétention explicite, **le disque se remplit et Prometheus s'arrête** — panne
silencieuse de toute la supervision.

> ⚠️ Ne dimensionnez pas à l'aveugle : vous avez désormais node-exporter et cAdvisor.
> Mesurez le pic réel sur 7 jours avant d'acheter (requêtes en §9).

---

## 4. Limites de ressources — le point le plus urgent

À poser **avant** toute mise en production, indépendamment de la taille des machines :

```yaml
services:
  api:
    mem_limit: 1g
    mem_reservation: 512m
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3
```

Sans plafond, un conteneur qui dérape emporte l'hôte. Avec plafond, **il meurt seul** et
Docker le redémarre : la panne reste locale et observable (`ContainerHighMemory` est déjà
dans vos règles d'alerte).

Ajoutez aussi une **limite de logs Docker** sur chaque service, sinon les fichiers
`json-file` grossissent sans fin :

```yaml
    logging:
      driver: json-file
      options: { max-size: "10m", max-file: "3" }
```

---

## 5. Base de données — le gain de performance le plus rentable

Votre base Neon est en **`eu-west-2` (AWS Londres)** ; le VPS est chez **Hetzner
Falkenstein (Allemagne)**. Chaque requête SQL traverse donc deux fournisseurs et deux
régions : ~15–25 ms d'aller-retour, **multipliés par le nombre de requêtes par appel
HTTP**. Un endpoint qui fait 8 requêtes paie 150 ms de latence pure réseau.

**Aucune quantité de RAM ne compense cela.** Migrer la base vers une région Neon proche
(Francfort) ramène ce coût à ~2–5 ms. C'est probablement le meilleur gain disponible,
avant même de changer de VPS.

À vérifier également : le **pooling** (Neon impose des limites de connexions ; utilisez le
pooler côté Neon plutôt que d'ouvrir 5 pools .NET indépendants) et le **SSL Mode=VerifyFull**
déjà présent dans la chaîne de connexion.

---

## 6. Réseau et sécurité

| Règle | Détail |
| --- | --- |
| **Réseau privé** entre A et B | Prometheus scrape les agents de A via IP privée ; les apps poussent l'OTLP vers B en privé. Rien ne transite par Internet. |
| **Pare-feu hôte A** | Ouvert : 80, 443 (+ 22 restreint). Rien d'autre. |
| **Pare-feu hôte B** | **Aucun port public.** Accès Grafana/Portainer par VPN (WireGuard) ou tunnel SSH. |
| **Portainer** | Contrôle Docker = root. Jamais exposé publiquement (Safe Browsing l'a d'ailleurs signalé), jamais en iframe. |
| **Grafana** | Sortir de l'accès anonyme dès qu'il n'est plus embarqué publiquement. |
| **Console admin** | Restreindre par IP allowlist Traefik en plus du JWT + MFA. |
| **Secrets** | Uniquement dans le vault chiffré. Jamais dans une image, jamais exposés par une API. |
| **Rotation** | Rotor les secrets déjà exposés (token GitHub, clé FCM) avant la prod. |

---

## 7. Déploiement

L'existant est sain : images construites par la CI et publiées sur **GHCR**, déploiement
**Ansible** (`--tags deploy` pour l'app, `--tags monitoring` pour l'observabilité),
secrets depuis le **vault**. À compléter :

**Zéro-downtime.** Aujourd'hui `docker compose up -d` recrée le conteneur : coupure de
quelques secondes. Pour l'éliminer, ajoutez un `healthcheck` (ci-dessus) et déployez en
deux temps (démarrer la nouvelle instance, attendre `healthy`, retirer l'ancienne) —
Traefik ne route que vers les conteneurs sains.

**Migrations de schéma.** `RunMigrationsAtStartup` est pratique mais dangereux avec
plusieurs instances : deux process peuvent migrer en même temps. En production, exécutez
les migrations comme **étape de déploiement dédiée**, avant de démarrer les apps.

---

## 8. Sauvegardes et reprise

| Donnée | Responsable | À vérifier |
| --- | --- | --- |
| PostgreSQL | Neon (PITR) | **Tester une restauration** — une sauvegarde jamais restaurée n'est pas une sauvegarde |
| Médias / documents | Cloudflare R2 | Versioning activé |
| Secrets | Vault (Git) | Copie hors dépôt de la passphrase |
| Métriques / logs | — | Non critiques : perte acceptable |
| Config serveur | Ansible (Git) | L'hôte est reconstructible en une commande |

Avantage de votre architecture : **rien d'irremplaçable n'est stocké sur les VPS**. Un
hôte détruit se reconstruit par Ansible ; les données vivent chez Neon et R2.

---

## 9. Mesurer avant d'acheter

```bash
# Pic de RAM réellement utilisée sur 7 jours (Go)
promtool query instant http://localhost:9090 \
  'max_over_time((node_memory_MemTotal_bytes - node_memory_MemAvailable_bytes)[7d:5m]) / 1024^3'

# Pic de CPU (en cœurs)
promtool query instant http://localhost:9090 \
  'max_over_time((count(node_cpu_seconds_total{mode="idle"}) - sum(rate(node_cpu_seconds_total{mode="idle"}[5m])))[7d:5m])'

# Croissance du disque (Go/jour)
promtool query instant http://localhost:9090 \
  'deriv(node_filesystem_size_bytes{mountpoint="/"}[1d] ) '
```

---

## 10. Chemin de montée en charge

**Étape 1 — Lancement (recommandé).** 2 hôtes (A + B) + Neon + R2. Point de défaillance
unique assumé sur A : un reboot = quelques minutes d'indisponibilité. Acceptable au
démarrage, et honnête à assumer plutôt que de sur-concevoir.

**Étape 2 — Haute disponibilité (quand le chiffre d'affaires le justifie).** Deux hôtes
applicatifs derrière un load balancer managé, Traefik en mode dynamique, Redis managé
(sinon il devient le nouveau point unique — vos sessions y vivent). L'observabilité reste
sur un seul hôte : sa panne n'interrompt pas le service.

**Étape 3 — Découplage.** Si un module devient un goulot (recherche, médias), il sort du
monolithe modulaire — l'architecture actuelle (frontières strictes, événements
d'intégration, outbox) a précisément été faite pour rendre cette extraction possible sans
réécriture.

---

## 11. Checklist avant mise en production

- [ ] Limites `mem_limit` + `healthcheck` + rotation des logs sur **tous** les conteneurs
- [ ] Rétentions explicites : Prometheus 30 j, Loki 14 j, Tempo 7 j
- [ ] Base Neon migrée dans une région proche du VPS
- [ ] Migrations sorties du démarrage applicatif
- [ ] Hôte B sans port public ; Grafana/Portainer derrière VPN
- [ ] Secrets compromis rotés (token GitHub, clé FCM)
- [ ] Canaux Alertmanager (Slack + e-mail) branchés et **testés**
- [ ] Restauration de sauvegarde Neon testée pour de vrai
- [ ] `dotnet build` + tests verts, images publiées sur GHCR
