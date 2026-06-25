# Déploiement — Traefik + monolithe + BFF

Traefik est le **point d'entrée unique** (edge). Il route le trafic vers les 4 BFF
et vers le monolithe `Marketplace.Api`, tous adossés à Postgres et Redis. Chaque
service est une composition in-process des 25 modules ; Traefik ne fait
qu'aiguiller, terminer la compression et appliquer en-têtes de sécurité +
limitation de débit.

```
                       ┌──────────── Traefik (:80) ────────────┐
   navigateur / app ──▶│  host-based            path-based     │
                       │  api.localhost         /api           │──▶ Marketplace.Api ─┐
                       │  mobile.localhost      /mobile         │──▶ Bff.Mobile       │
                       │  web.localhost         /web            │──▶ Bff.Web          ├─▶ Postgres
                       │  seller.localhost      /seller         │──▶ Bff.Seller       │   Redis
                       │  admin.localhost       /admin          │──▶ Bff.Admin        ┘
                       └───────────────────────────────────────┘
                          dashboard : http://traefik.localhost (ou :8080)
```

## Démarrer

```bash
cd deploy
cp .env.example .env          # puis renseigne POSTGRES_PASSWORD et JWT_SIGNING_KEY
docker compose up --build
```

Au démarrage, `api` applique les migrations de tous les modules (`RunMigrationsAtStartup=true`),
puis les BFF démarrent une fois l'`api` saine.

## Accès

Deux façons d'atteindre la même surface (les `*.localhost` pointent vers 127.0.0.1) :

| Surface | Par hôte (avec Swagger)        | Par préfixe (gateway unique)      |
|---------|--------------------------------|-----------------------------------|
| Monolithe | http://api.localhost/swagger | http://localhost/api/...          |
| Mobile  | http://mobile.localhost/swagger | http://localhost/mobile/...      |
| Web     | http://web.localhost/swagger   | http://localhost/web/...          |
| Vendeur | http://seller.localhost/swagger | http://localhost/seller/...      |
| Admin   | http://admin.localhost/swagger | http://localhost/admin/...        |

Dashboard Traefik : http://traefik.localhost ou http://localhost:8080.

## Notes

- **JWT** : `JWT_SIGNING_KEY` (dans `.env`) est injecté à l'identique dans les 5
  services via `Jwt__SigningKey` — indispensable pour que les jetons émis par
  l'Identity soient validés partout. Les variables d'environnement priment sur
  les `appsettings.json`, donc tes secrets locaux ne sont pas utilisés ici.
- **Base de données** : la chaîne de connexion est surchargée vers le service
  `postgres` (port interne 5432). Le volume `pgdata` persiste les données.
- **Traefik** : `api.insecure: true` et le dashboard sur `:8080` sont réservés au
  développement. Avant un déploiement public : passer en HTTPS (entrypoint
  `websecure` + résolveur ACME), retirer l'accès insecure, et restreindre le CORS
  de la console admin (`Cors:Origins`).
- **Outbox** : chaque service (Api + 4 BFF) embarque les modules et leurs
  processeurs d'arrière-plan. Faire tourner les 5 en parallèle sur la même base
  est pratique en dev ; en production, dédier le traitement d'arrière-plan à un
  seul processus (ou désactiver les workers dans les BFF) pour éviter la
  concurrence sur l'outbox.
```
