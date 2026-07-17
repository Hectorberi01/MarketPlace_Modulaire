# Monitoring — HbaExpress

Stack d'observabilité self-hostée pour le backend .NET (API + BFF). Elle consomme
l'observabilité déjà câblée dans les applications :

- **Traces + métriques** : les apps exportent en **OTLP** vers l'OpenTelemetry Collector.
- **Logs** : JSON compact sur stdout (Serilog) → récupérés depuis Docker par Promtail.

## Composants

| Service          | Rôle                                        | Port (hôte) |
|------------------|---------------------------------------------|-------------|
| otel-collector   | Ingestion OTLP (traces + métriques)         | 4317 / 4318 |
| prometheus       | Stockage métriques                          | (interne)   |
| tempo            | Stockage traces                             | (interne)   |
| loki             | Stockage logs                               | (interne)   |
| promtail         | Expédition des logs Docker → Loki           | (interne)   |
| grafana          | Dashboards + exploration                    | **3000**    |
| uptime-kuma      | Supervision d'uptime + alertes              | **3001**    |

## Démarrage

```bash
cd deploy/monitoring
cp .env.monitoring.example .env      # définir GRAFANA_PASSWORD
docker compose -f docker-compose.monitoring.yml up -d
```

- Grafana : http://localhost:3000 (datasources Prometheus/Loki/Tempo déjà branchées).
- Uptime Kuma : http://localhost:3001 (créer le compte au 1er lancement).

## Brancher les applications

Deux choses côté stack applicative (`docker-compose.staging.yml` / `prod`) :

1. **Exporter la télémétrie** : définir l'endpoint OTLP sur chaque service API/BFF
   ```yaml
   environment:
     - Otel__Endpoint=http://otel-collector:4317
     # (équivalent : OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317)
   ```

2. **Partager le réseau** pour que les conteneurs joignent `otel-collector`.
   Le réseau `marketplace-monitoring` est créé par ce compose ; déclarez-le en
   **externe** dans le compose applicatif et attachez-y les services :
   ```yaml
   services:
     api:
       networks: [default, marketplace-monitoring]
     # … idem pour chaque bff
   networks:
     marketplace-monitoring:
       external: true
   ```

Les logs sont récupérés automatiquement par Promtail (aucune config par app).

## Health checks

Les apps exposent désormais :
- `GET /health` — **liveness** (le process répond) → à utiliser pour le `healthcheck` Docker.
- `GET /health/ready` — **readiness** (Postgres + Redis OK) → à surveiller dans Uptime Kuma.

Dans Uptime Kuma, ajoutez un moniteur HTTP par surface (api, m, seller, admin, web)
pointant sur `https://<host>/health/ready`, avec alertes (email/Telegram/Slack).

## Dashboards Grafana (à importer)

Datasources prêtes ; importez des dashboards existants (Dashboards → Import, par ID) :
- **19924** / **19925** — ASP.NET Core (OpenTelemetry).
- **1860** — Node/host (si vous ajoutez node-exporter).
Puis explorez les traces dans **Tempo** et les logs dans **Loki** (corrélation trace↔log active).

## Embarquer un dashboard dans la console admin (page « Santé système »)

La page Système peut afficher un dashboard Grafana **en iframe**. Deux réglages :

1. **Côté Grafana** : `GF_SECURITY_ALLOW_EMBEDDING=true` (déjà dans le compose). Pour
   un affichage sans login (console interne), activez l'accès anonyme Viewer
   (variables `GF_AUTH_ANONYMOUS_*` commentées dans le compose).
2. **Côté BFF Admin** : renseignez l'URL d'embed dans l'appsettings :
   ```json
   "Monitoring": {
     "GrafanaUrl": "https://grafana.interne",
     "GrafanaEmbedUrl": "https://grafana.interne/d/<uid>/aspnet?orgId=1&kiosk&theme=light"
   }
   ```
   Le paramètre `kiosk` masque le chrome Grafana ; `theme=light` s'accorde à la console.
   Sans `GrafanaEmbedUrl`, la page affiche seulement la santé + le bouton « Ouvrir Grafana ».

## ⚠️ Sécurité

Ne publiez pas Grafana/Prometheus/Loki/Tempo/Uptime-Kuma sur Internet. Gardez-les
sur un réseau interne / VPN, ou derrière Traefik **avec authentification** (basic
auth ou SSO). Changez le mot de passe Grafana par défaut.
