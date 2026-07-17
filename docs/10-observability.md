# Observabilité — Métriques applicatives (.NET / OpenTelemetry)

Ce document décrit la couche de **métriques métier et techniques** ajoutée à l'API
(`Marketplace.Api`). Elle s'appuie sur l'observabilité déjà en place (Serilog → Loki,
OpenTelemetry traces + métriques → Collector → Prometheus, health checks) et vient la
compléter par des métriques de **disponibilité**, **paiements**, **marketplace** et
**sécurité**.

> Convention : on garde les noms OpenTelemetry natifs pour le HTTP
> (`http_server_request_duration_seconds*`, déjà exportés) et on **ajoute** des
> métriques custom. Aucune métrique n'est dupliquée.

---

## 1. Architecture

```
Apps .NET ──(OTLP)──> OpenTelemetry Collector ──(:8889 /metrics)──> Prometheus ──> Grafana
   │  Serilog (stdout JSON) ─> Promtail ─> Loki ─────────────────────────────────> Grafana
   │  Traces ────────────────> Collector ─> Tempo ────────────────────────────────> Grafana
VPS    ─> node-exporter  (:9100) ──────────> Prometheus
Docker ─> cAdvisor       (:8080) ──────────> Prometheus
Redis  ─> redis-exporter (:9121) ──────────> Prometheus
                                             Prometheus ──(alertes)──> Alertmanager ─> Slack + e-mail
Console admin (page Système) ──> BFF Admin ──> Prometheus (KPIs, vue Conteneurs)
                             └─> iframes Grafana (4 dashboards)
```

Les métriques custom passent par le **même pipeline OTLP** que les métriques natives :
il suffit de déclarer leur *meter* à OpenTelemetry (fait) pour qu'elles apparaissent
dans Prometheus via le Collector. Aucun endpoint `/metrics` supplémentaire n'est requis
côté API (le Collector est la surface de scrape).

### Services de la stack (`deploy/monitoring/docker-compose.monitoring.yml`)

| Service | Rôle |
| --- | --- |
| `otel-collector` | Ingestion OTLP (4317/4318) → Tempo (traces) + exposition Prometheus (:8889) |
| `prometheus` | Métriques + évaluation des règles d'alerte (rétention 15 j) |
| `alertmanager` | Routage + notifications des alertes (Slack, e-mail) |
| `tempo` | Traces |
| `loki` + `promtail` | Logs (Serilog JSON récupérés depuis Docker) |
| `grafana` | Dashboards (provisionnés) — exposé via Traefik/TLS |
| `cadvisor` | Métriques par conteneur |
| `node-exporter` | Métriques de l'hôte VPS |
| `redis-exporter` | Métriques Redis |
| `uptime-kuma` | Ping des `/health` |

La stack rejoint le **réseau Docker externe** de l'app (`marketplace-staging-net`) :
les apps joignent `otel-collector`, le BFF Admin joint `prometheus`, Traefik route `grafana`.

---

## 2. Packages NuGet

Déjà référencés (via `Directory.Packages.props`, versions centralisées) :

| Package | Rôle |
| --- | --- |
| `OpenTelemetry.Extensions.Hosting` | Intégration DI / hosting |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Export OTLP (métriques + traces) |
| `OpenTelemetry.Instrumentation.AspNetCore` | Métriques/traces HTTP serveur |
| `OpenTelemetry.Instrumentation.Http` | Métriques/traces HttpClient |
| `OpenTelemetry.Instrumentation.Runtime` | GC, threads, heap .NET (`dotnet_*` / `process_*`) |
| `AspNetCore.HealthChecks.NpgSql`, `.Redis` | Sondes Postgres / Redis |
| `Serilog.AspNetCore`, `Serilog.Formatting.Compact` | Logs structurés JSON |

Les métriques custom utilisent **`System.Diagnostics.Metrics`** (BCL, via
`IMeterFactory`) — aucun package supplémentaire.

---

## 3. Configuration ASP.NET Core

Dans `Program.cs` de chaque hôte :

```csharp
builder.AddObservability("marketplace-api", businessMetrics: true); // API
// BFF : builder.AddObservability("bff-admin"); (businessMetrics: false par défaut)
...
app.MapObservability();
```

`AddObservability(...)` (voir `Observability/ObservabilityExtensions.cs`) :

- configure Serilog (JSON compact sur stdout) ;
- enregistre les health checks Postgres/Redis (tag `ready`) ;
- enregistre les meters custom + `IMeterFactory` (`AddMetrics()`) ;
- déclare les meters à OpenTelemetry (`AddMeter(MarketplaceMeters.All)`) ;
- configure traces + métriques OTel, export OTLP si `Otel:Endpoint` (ou
  `OTEL_EXPORTER_OTLP_ENDPOINT`) est défini.

`MapObservability(app)` : logging requêtes, middleware sécurité (401/403/429),
exposition `/health` (liveness) et `/health/ready` (readiness), et instancie les
meters au démarrage.

### Health checks — statuts

`/health/ready` agrège les sondes taguées `ready`. Chaque sonde produit `Healthy`,
`Degraded` ou `Unhealthy`, converti en `dependency_health_status` (1 / 0.5 / 0) par
`DependencyHealthPublisher`. **Ajouter une sonde** (Kafka, fournisseur de paiement…)
suffit à la voir apparaître :

```csharp
health.AddKafka(kafkaOptions, name: "kafka", tags: new[] { "ready" });
health.AddUrlGroup(new Uri(fedapayHealthUrl), name: "fedapay", tags: new[] { "ready" });
```

---

## 4. Catalogue des métriques custom

> Les compteurs sont nommés **sans** `_total` dans le code ; l'exporter Prometheus
> ajoute le suffixe (`payment_attempts` → `payment_attempts_total`).

### 4.1 Disponibilité — meter `Marketplace.Availability`

| Métrique (Prometheus) | Type | Labels | Sens |
| --- | --- | --- | --- |
| `api_up` | gauge | `service_name` | 1 = process vivant |
| `application_uptime_seconds` | gauge | `service_name` | secondes depuis le démarrage |
| `application_restart_total` | counter | `service_name` | +1 au démarrage (redémarrages via `resets()`) |
| `dependency_health_status` | gauge | `dependency` | 1 healthy / 0.5 degraded / 0 unhealthy |

### 4.2 Paiements — meter `Marketplace.Payments`

Labels autorisés : `provider`, `currency`, `status`, `payment_method`, `failure_reason`.

| Métrique | Type | Notes |
| --- | --- | --- |
| `payment_attempts_total` | counter | paiements initiés |
| `payment_success_total` | counter | réussis |
| `payment_failed_total` | counter | échoués (+ `failure_reason`) |
| `payment_pending_total` | counter | en attente |
| `payment_cancelled_total` | counter | annulés |
| `payment_amount_total` | counter | montant réussi, **plus petite unité**, par `currency` |
| `payment_refund_total` | counter | remboursé, plus petite unité, par `currency` |
| `payment_webhook_errors_total` | counter | erreurs de webhook |
| `payment_duration_seconds` | histogram | durée du paiement |
| `payment_webhook_duration_seconds` | histogram | délai de traitement webhook |

### 4.3 Marketplace — meter `Marketplace.Business`

| Métrique | Type | Notes |
| --- | --- | --- |
| `users_registered_total` | counter | inscriptions |
| `products_created_total` | counter | produits créés |
| `carts_created_total` / `carts_abandoned_total` | counter | paniers |
| `orders_created_total` / `orders_completed_total` / `orders_cancelled_total` | counter | commandes |
| `marketplace_revenue_total` | counter | CA, plus petite unité, par `currency` |
| `platform_commission_total` | counter | commissions, plus petite unité, par `currency` |
| `users_active` / `vendors_active` / `products_available` | gauge | état courant (publier via `SetGauge`) |

### 4.4 Sécurité — meter `Marketplace.Security`

Labels autorisés : `authentication_method`, `failure_reason`, `client_type`, `route`
(motif de route, **jamais** l'URL brute).

| Métrique | Type |
| --- | --- |
| `login_success_total` / `login_failed_total` | counter |
| `user_registration_total` | counter |
| `password_reset_total` | counter |
| `unauthorized_requests_total` (401) | counter |
| `forbidden_requests_total` (403) | counter |
| `rate_limit_exceeded_total` (429) | counter |
| `account_locked_total` | counter |
| `token_validation_failed_total` | counter |

401 / 403 / 429 sont émis **automatiquement** par `SecurityMetricsMiddleware`.

---

## 5. Points d'émission (où appeler les métriques)

Le middleware (401/403/429), la disponibilité et la santé des dépendances sont
**automatiques**. Les émissions métier sont exposées via des **interfaces dans
`BuildingBlocks.Application.Observability`** (`IPaymentMetrics`,
`IMarketplaceBusinessMetrics`, `ISecurityMetrics`) implémentées par les classes
concrètes de l'API — les modules les injectent sans dépendre de l'hôte.

**Déjà câblé** ✅ :

- `login_success` / `login_failed` (+ raison) — `LoginCommandHandler`.
- `user_registration` + `users_registered` — `RegisterUserCommandHandler`.
- `password_reset` — `RequestPasswordResetCommandHandler`.
- `payment_success` / `payment_failed` / `payment_refund` (+ montant plus petite
  unité par devise, provider, méthode) — `PaymentDomainEventHandlers` (événements
  de domaine enrichis).
- `orders_completed` + `marketplace_revenue` — `AccrueMetricsOnOrderConfirmedHandler`.

**Reste à câbler** (mêmes interfaces, emplacements ci-dessous) :

| Métrique | Où l'émettre | Appel |
| --- | --- | --- |
| `login_success` / `login_failed` | Identity — handler de login | `security.LoginSuccess("password","web")` |
| `user_registration` / `users_registered` | Identity — `UserRegisteredIntegrationEvent` | `security.Registration(...)` + `business.UserRegistered()` |
| `password_reset` | Identity — reset mot de passe | `security.PasswordReset()` |
| `account_locked` | Identity — lockout | `security.AccountLocked("too_many_attempts")` |
| `payment_*` | Payments — handler d'encaissement / webhook | `payments.Success(provider, method, currency, minor, dur)` |
| `payment_webhook_*` | Payments — réception webhook | `payments.WebhookError(...)` / `WebhookProcessed(...)` |
| `orders_created/completed/cancelled`, `marketplace_revenue`, `platform_commission` | Ordering / Settlement — `OrderConfirmedIntegrationEvent` | `business.OrderCompleted(currency, revMinor, commMinor)` |
| `carts_created` / `carts_abandoned` | Cart — création / job d'abandon | `business.CartCreated()` / `CartAbandoned()` |
| `products_created` | Catalog — `ProductCreatedIntegrationEvent` | `business.ProductCreated()` |
| `users_active` / `vendors_active` / `products_available` | Job périodique (lecture BDD) | `business.SetGauge("users_active", n)` |

> **Contrainte d'architecture** : les classes de métriques vivent dans
> `Marketplace.Api` (racine de composition). Les émetteurs qui consomment des
> *integration events* se placent donc soit dans `Marketplace.Api` (qui référence les
> Contracts des modules), soit — pour émettre au plus près — on expose les métriques
> derrière une interface dans un projet `BuildingBlocks` référencé par les modules.
> Voir le lot « émission métier » pour le câblage complet.

Exemple d'émetteur (dans `Marketplace.Api`, event-driven) :

```csharp
public sealed class EmitBusinessMetricsOnOrderConfirmed
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly MarketplaceBusinessMetrics _metrics;
    private readonly IOrderingModuleApi _ordering;

    public EmitBusinessMetricsOnOrderConfirmed(MarketplaceBusinessMetrics metrics, IOrderingModuleApi ordering)
        => (_metrics, _ordering) = (metrics, ordering);

    public async Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken ct = default)
    {
        var order = await _ordering.GetOrderAsync(e.OrderId, ct);
        if (order is null) return;
        var revenueMinor    = (long)(order.GrandTotal * 100);   // EUR/USD : centimes ; adapter par devise
        var commissionMinor = (long)(order.PlatformCommission * 100);
        _metrics.OrderCompleted(order.Currency, revenueMinor, commissionMinor);
    }
}
// Enregistrement (Program.cs de l'API) :
// services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, EmitBusinessMetricsOnOrderConfirmed>();
```

---

## 6. Exemples PromQL

**Disponibilité**
```promql
api_up                                            # 1 = up
avg_over_time(api_up[30d]) * 100                  # % de disponibilité (30j)
application_uptime_seconds / 3600                 # uptime en heures
resets(application_restart_total[24h])            # nb de redémarrages sur 24h
dependency_health_status{dependency="postgres"}   # 1/0.5/0
```

**Trafic HTTP (métriques OTel natives)**
```promql
sum by (exported_job) (rate(http_server_request_duration_seconds_count[5m]))   # req/s par service
topk(10, sum by (http_route) (rate(http_server_request_duration_seconds_count[5m])))  # endpoints les + sollicités
```

**Latence (histogramme natif)**
```promql
histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket[5m])))  # P95 par route
histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))              # P99 global
```

**Erreurs**
```promql
sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
/ sum(rate(http_server_request_duration_seconds_count[5m])) * 100          # taux 5xx (%)
```

**Paiements**
```promql
sum by (provider) (rate(payment_success_total[5m]))
sum(rate(payment_failed_total[15m])) / sum(rate(payment_attempts_total[15m])) * 100   # taux d'échec %
histogram_quantile(0.95, sum by (le, provider) (rate(payment_duration_seconds_bucket[5m])))
sum by (currency) (increase(payment_amount_total[1d]))                                  # volume/jour par devise
```

**Marketplace**
```promql
increase(marketplace_revenue_total[1d])                                                # CA du jour (minor units)
increase(marketplace_revenue_total[1d]) / increase(orders_completed_total[1d])         # panier moyen
increase(orders_completed_total[1d]) / increase(carts_created_total[1d]) * 100          # conversion %
increase(orders_cancelled_total[1d]) / increase(orders_created_total[1d]) * 100         # annulation %
```

**Sécurité**
```promql
sum(rate(login_failed_total[5m]))
sum by (route) (rate(unauthorized_requests_total[5m]))
sum(rate(rate_limit_exceeded_total[5m]))
```

**.NET runtime / Redis / Node / Docker**
```promql
process_memory_usage_bytes / 1024 / 1024                              # RSS (Mo)
rate(dotnet_gc_collections_total[5m])                                 # collections GC/s
redis_keyspace_hits_total / (redis_keyspace_hits_total + redis_keyspace_misses_total) * 100   # hit rate
100 - (avg by (instance) (rate(node_cpu_seconds_total{mode="idle"}[5m])) * 100)               # CPU VPS %
sum by (image) (rate(container_cpu_usage_seconds_total{name!=""}[5m]))                          # CPU par conteneur
```

---

## 7. Alertes (`deploy/monitoring/prometheus-rules.yml`)

**22 règles** réparties en 7 groupes, définies dans `deploy/monitoring/prometheus-rules.yml`
(chargé via `rule_files` et monté dans le conteneur Prometheus).

| Groupe | Alertes | Sévérité |
| --- | --- | --- |
| `api-availability` | ApiDown (`api_up == 0`, 1 min) · DependencyUnhealthy (`dependency_health_status < 1`, 2 min) · FrequentRestarts | critique / warning |
| `http-errors-latency` | High5xxErrorRate (> 2 %, 5 min) · EndpointHigh5xx · HighLatencyP95 (> 1,5 s, 10 min) | critique / warning |
| `host-vps` | HostHighCpu (> 80 %) · HostHighMemory (> 85 %) · HostDiskWarning (> 80 %) · HostDiskCritical (> 90 %) · NodeExporterDown | warning / critique |
| `containers` | ContainerHighCpu · ContainerHighMemory (> 85 % de la limite) | warning |
| `redis` | RedisDown · RedisHighMemory (> 80 %) · RedisEvictions · RedisLowHitRate (< 80 %) | critique / warning |
| `payments` | PaymentHighFailureRate (> 20 %, 15 min) · PaymentWebhookErrors | critique / warning |
| `security` | LoginFailureSpike · FrequentRateLimiting · UnauthorizedForbiddenSpike | warning |

Les règles à ratio incluent un **garde-fou de trafic** (`and sum(rate(...)) > seuil`) pour
éviter les faux positifs quand il n'y a pas d'activité.

Validation : `docker exec <prometheus> promtool check rules /etc/prometheus/prometheus-rules.yml`

### Notifications — Alertmanager

`alertmanager` route les alertes déclenchées :

- `severity=critical` → **Slack + e-mail** (repeat 1 h)
- `severity=warning` → **Slack** (repeat 4 h)
- groupement par `alertname` + `severity`, `group_wait` 30 s

La config est **générée par Ansible** (`roles/monitoring/templates/alertmanager.yml.j2`)
depuis le **vault** — elle contient des secrets, donc elle n'est **jamais committée** et
est exclue de la synchro rsync. `deploy/monitoring/alertmanager.yml.example` documente le format.

Variables requises :

| Où | Clé |
| --- | --- |
| **vault** | `alertmanager_slack_webhook_url`, `smtp_password` |
| `group_vars` | `alert_slack_channel`, `smtp_host`, `smtp_port`, `smtp_from`, `smtp_username`, `alert_email_to` |

Une valeur vide désactive le canal correspondant. Test :
`docker exec <alertmanager> amtool alert add test severity=critical --annotation=summary="Test"`

---

## 8. Dashboards Grafana (provisionnés)

Quatre dashboards à **UID fixe**, versionnés dans
`deploy/monitoring/grafana/provisioning/dashboards/json/` (rechargés automatiquement
par Grafana, `updateIntervalSeconds: 30`) :

| UID | Titre | Contenu |
| --- | --- | --- |
| `app-overview` | Apps .NET (OpenTelemetry) | Débit, latence P95, erreurs 5xx, connexions Kestrel — ventilé par `exported_job` |
| `containers` | Conteneurs (cAdvisor) | CPU + mémoire par image, réseau par interface |
| `node-vps` | VPS (Node Exporter) | CPU, RAM, disque, load, réseau, descripteurs de fichiers |
| `redis` | Redis | Mémoire, clients, commandes/s, hit rate, expirations/évictions, clés |

> Les métriques applicatives sont ventilées par **`exported_job`** (et non `service_name`) :
> l'exporter Prometheus du Collector mappe `service.name` → `job`, que Prometheus renomme
> `exported_job` (collision avec son propre job de scrape).

---

## 9. Intégration dans la console admin (page Système)

La page `/systeme` de la console admin agrège la supervision :

- **KPIs** (débit, taux d'erreur, latence p95) et **santé des surfaces** — via le BFF Admin qui interroge Prometheus et les `/health`.
- **Vue « Conteneurs » native, en LECTURE SEULE** — endpoint `/admin/monitoring/containers` : le BFF interroge Prometheus/cAdvisor (CPU, mémoire, uptime, par image). **Aucun socket Docker n'est exposé au BFF** — la console ne peut donc ni démarrer, ni arrêter, ni supprimer un conteneur. C'est délibéré : le socket Docker équivaut à un accès root sur l'hôte.
- **Onglets Grafana** — les 4 dashboards ci-dessus en iframe, l'onglet actif pilotant l'URL d'embed (construite depuis `Monitoring:GrafanaUrl`).
- **Configuration en lecture seule** — clés non sensibles en clair, secrets réduits à « défini / non défini ». Toute modification passe par le **vault + déploiement**, jamais par l'API.

Pour l'**embed Grafana**, trois réglages sont nécessaires : `GF_SECURITY_ALLOW_EMBEDDING=true`,
`GF_AUTH_ANONYMOUS_ENABLED=true` (rôle Viewer), et une chaîne Traefik dédiée
(`edge-chain-embed`) **sans** `frameDeny` — sinon l'en-tête `X-Frame-Options: DENY` du reverse
proxy écrase l'autorisation de Grafana.

---

## 10. Déploiement (tags Ansible)

| Tag | Ce qu'il fait |
| --- | --- |
| `--tags deploy` | Synchronise le code, régénère `.env.<env>`, build/up de la stack **applicative** |
| `--tags monitoring` | Synchronise les fichiers de conf **+** `up -d` de la stack monitoring **+** redémarre `otel-collector`, `prometheus`, `promtail`, `alertmanager`, `grafana` |

⚠️ **Piège classique** : `--tags deploy` synchronise bien les fichiers (Grafana peut donc
charger un nouveau dashboard) **mais n'exécute pas le rôle monitoring** — les nouveaux
exporters et jobs de scrape ne sont pas créés. Pour tout changement d'observabilité :
`--tags monitoring` (ou `--tags deploy,monitoring`).

Autre piège : les fichiers de conf montés (prometheus.yml, promtail, provisioning Grafana)
ne sont relus qu'**au démarrage** — un simple `up -d` ne les recharge pas si la définition
du service est inchangée. D'où le `restart` explicite dans le rôle.

---

## 11. Limitations connues (spécifiques à cet hôte)

- **cAdvisor + snapshotter containerd** — ce serveur utilise le driver `overlayfs`
  (image store containerd) : le handler Docker de cAdvisor ne peut pas s'initialiser
  (« failed to identify the read-write layer ID ») et avorte la création de chaque
  conteneur. Contournement appliqué : cAdvisor est basculé sur la **factory containerd**
  (`--docker=unix:///dev/null`, `--containerd-namespace=moby`). Conséquences :
  le label `name` contient l'**ID brut** du conteneur → on ventile par **`image`** ;
  et le **réseau par conteneur n'est pas exposé** → les panneaux réseau utilisent les
  interfaces de l'hôte (`id="/"`, label `interface`).
- **Promtail** — les clés Serilog `@l` / `@m` ne sont pas des identifiants JMESPath
  valides : elles doivent être **entre guillemets** dans le stage `json` (`'"@l"'`).
  Les positions sont persistées sur un volume (sinon chaque redémarrage relit tout
  l'historique → tempête d'ingestion + 429 côté Loki).

---

## 12. Bonnes pratiques — sécurité des métriques

**Ne JAMAIS mettre en label** (Prometheus ou attribut de span) :
numéro de carte, téléphone, email, nom, identifiant client/utilisateur, IP complète,
token, session id, secret de paiement, contenu de webhook.

- Pour l'analyse fine par IP/utilisateur → **Loki + logs structurés**, pas Prometheus.
- Les valeurs financières restent des **entiers en plus petite unité** (pas de float),
  ventilées par `currency` — jamais d'addition inter-devises (EUR ≠ XOF ≠ USD).
  Le helper partagé `MoneyUnits.ToMinorUnits` ne multiplie **pas** les devises « zéro
  décimale » (XOF, XAF, JPY…) par 100 — crucial pour XOF (FedaPay).

### Exposition des outils d'administration

- **Aucun service de la stack monitoring n'est publié sur un port hôte** (sauf Grafana
  via Traefik/TLS). Prometheus, Alertmanager, Loki, Tempo ne sont joignables que sur le
  réseau Docker interne.
- **Le socket Docker n'est JAMAIS monté dans le BFF.** La vue « Conteneurs » de la console
  est en lecture seule, alimentée par Prometheus. Les vendeurs ne voient ni conteneurs ni
  variables d'environnement.
- **Portainer contrôle le démon Docker = accès root sur l'hôte.** Il ne doit **pas** être
  exposé publiquement (une page de login d'admin auto-hébergée sur un sous-domaine récent
  se fait d'ailleurs régulièrement signaler par Google Safe Browsing). Le restreindre par
  **IP allowlist Traefik** ou n'y accéder que par **tunnel SSH**. Ne jamais l'embarquer en
  iframe : ses protections anti-clickjacking (`X-Frame-Options`) existent pour une raison.
- **Grafana** est aujourd'hui en **anonymous Viewer** sur un domaine public (nécessaire pour
  l'embed dans la console) → à restreindre par VPN/IP en production.
- Les secrets ne transitent **jamais** par une API : modification via le **vault chiffré**
  puis redéploiement.

## 13. Bonnes pratiques — cardinalité des labels

- Labels **bornés** uniquement : `provider`, `currency`, `status`, `payment_method`,
  `method`, `route` (motif), `failure_reason` (ensemble fini).
- **Jamais** l'URL complète, un id, un timestamp, un email en label → explosion de
  séries. Utiliser le **motif de route** (`/orders/{id}`), pas le chemin résolu.
- `failure_reason` : mapper vers un ensemble fini (`insufficient_funds`,
  `provider_error`, `timeout`, `declined`, `invalid_token`, `unknown`).
- Un histogramme = déjà `_bucket` + `_sum` + `_count` : éviter de le croiser avec des
  labels à forte cardinalité.
