#!/usr/bin/env bash
# =====================================================================
#  Sauvegarde PostgreSQL (base co-hébergée) → Cloudflare R2
#
#  Fait un `pg_dump` logique compressé, l'envoie dans un bucket R2 PRIVÉ,
#  et purge les anciennes copies locales. C'est le VRAI filet de sécurité :
#  la sauvegarde quotidienne du VPS (snapshot de volume actif) ne garantit
#  pas un état Postgres cohérent, et sa granularité est de 24 h.
#
#  À planifier via cron sur l'hôte, ex. toutes les 6 h :
#     0 */6 * * *  /opt/marketplace/deploy/postgres/backup.sh >> /var/log/mp-backup.log 2>&1
#
#  Restauration (exemple) :
#     gunzip -c marketplace-YYYYmmdd-HHMMSS.sql.gz \
#       | docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d marketplace
#
#  Prérequis : Docker (aucune install aws-cli sur l'hôte — on passe par un
#  conteneur jetable). Les identifiants R2 sont lus dans le .env de prod.
#
#  Rétention DISTANTE : configure une règle de cycle de vie sur le bucket R2
#  (expiration après N jours) — plus fiable qu'une purge scriptée.
# =====================================================================
set -euo pipefail

# --------------------------- réglages --------------------------------
APP_DIR="${APP_DIR:-/opt/marketplace}"
COMPOSE_FILE="${COMPOSE_FILE:-$APP_DIR/deploy/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-$APP_DIR/deploy/.env.prod}"
BACKUP_DIR="${BACKUP_DIR:-$APP_DIR/backups}"
LOCAL_RETENTION_DAYS="${LOCAL_RETENTION_DAYS:-7}"
BACKUP_BUCKET="${BACKUP_BUCKET:-hbaexpress-backups}"
BACKUP_PREFIX="${BACKUP_PREFIX:-postgres}"
PG_SERVICE="${PG_SERVICE:-postgres}"

log() { printf '%s  %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"; }
die() { printf '%s  ERREUR: %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >&2; exit 1; }

[ -f "$COMPOSE_FILE" ] || die "compose introuvable : $COMPOSE_FILE"
[ -f "$ENV_FILE" ]     || die "env introuvable : $ENV_FILE"
command -v docker >/dev/null || die "docker absent"

# Lit une variable dans le .env (dernière occurrence, valeur après le 1er '=').
get_env() { grep -E "^$1=" "$ENV_FILE" | tail -n1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'; }

PG_DB="$(get_env POSTGRES_DB)";   PG_DB="${PG_DB:-marketplace}"
PG_USER="$(get_env POSTGRES_USER)"; PG_USER="${PG_USER:-postgres}"
R2_ACCOUNT_ID="$(get_env R2_ACCOUNT_ID)"
R2_ACCESS_KEY_ID="$(get_env R2_ACCESS_KEY_ID)"
R2_SECRET_ACCESS_KEY="$(get_env R2_SECRET_ACCESS_KEY)"

mkdir -p "$BACKUP_DIR"
STAMP="$(date -u +'%Y%m%d-%H%M%S')"
FILE="marketplace-$STAMP.sql.gz"
DEST="$BACKUP_DIR/$FILE"

# --------------------------- dump ------------------------------------
log "pg_dump de « $PG_DB » → $DEST"
# `exec -T` : pas de TTY (cron). Connexion locale au socket du conteneur = trust,
# donc pas de mot de passe requis pour le dump.
if ! docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T "$PG_SERVICE" \
      pg_dump -U "$PG_USER" -d "$PG_DB" --no-owner --no-privileges \
      | gzip -c > "$DEST"; then
  rm -f "$DEST"
  die "pg_dump a échoué"
fi

# Garde-fou : un dump vide/tronqué ne doit jamais partir comme « sauvegarde ».
SIZE=$(stat -c%s "$DEST" 2>/dev/null || stat -f%z "$DEST")
[ "${SIZE:-0}" -gt 100 ] || die "dump anormalement petit ($SIZE octets) — annulé"
log "dump OK ($SIZE octets)"

# --------------------------- upload R2 -------------------------------
if [ -n "$R2_ACCOUNT_ID" ] && [ -n "$R2_ACCESS_KEY_ID" ] && [ -n "$R2_SECRET_ACCESS_KEY" ]; then
  log "envoi vers r2://$BACKUP_BUCKET/$BACKUP_PREFIX/$FILE"
  docker run --rm \
    -e AWS_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
    -e AWS_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
    -e AWS_DEFAULT_REGION=auto \
    -v "$BACKUP_DIR:/backup:ro" \
    amazon/aws-cli \
    s3 cp "/backup/$FILE" "s3://$BACKUP_BUCKET/$BACKUP_PREFIX/$FILE" \
    --endpoint-url "https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com" \
    && log "upload R2 OK" \
    || die "upload R2 échoué (le dump local est conservé : $DEST)"
else
  log "⚠ identifiants R2 absents du .env — dump conservé UNIQUEMENT en local"
fi

# --------------------------- purge locale ----------------------------
log "purge locale > ${LOCAL_RETENTION_DAYS} j"
find "$BACKUP_DIR" -name 'marketplace-*.sql.gz' -type f -mtime "+$LOCAL_RETENTION_DAYS" -print -delete || true

log "terminé."
