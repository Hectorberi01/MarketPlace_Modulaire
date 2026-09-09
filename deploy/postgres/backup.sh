#!/usr/bin/env bash
# ═════════════════════════════════════════════════════════════════════════════════
# SAUVEGARDE DE LA BASE DE PRODUCTION.
#
# S'exécute SUR LE VPS BASE, en local, sans Docker. La version précédente faisait
# `docker compose exec postgres pg_dump` : elle supposait que la base était un
# conteneur sur la machine applicative. Depuis que la base vit sur sa propre
# machine, cette commande ne dumperait plus rien — ou pire, dumperait le conteneur
# local resté allumé, c'est-à-dire une base vide, et le dump serait déclaré valide.
#
# Installé et planifié par le rôle Ansible `backup`. Configuration dans
# /etc/marketplace-backup.env.
#
# ─────────────────────────────────────────────────────────────────────────────────
# CE QU'IL SAUVEGARDE, ET POURQUOI CES TROIS CHOSES.
#
#   1. Les RÔLES et leurs mots de passe (`pg_dumpall --globals-only`). Sans eux, une
#      base restaurée sur un cluster neuf n'a pas de compte applicatif : la
#      restauration « réussit » et l'application ne peut pas se connecter.
#   2. La base de la place de marché.
#   3. La base du dispatch. Séparée, donc oubliée si on ne la nomme pas.
#
# FORMAT `custom` (-Fc), pas du SQL compressé. Il permet `pg_restore --jobs`, qui
# divise par plusieurs le temps de restauration d'une base à vingt-six schémas, et
# la restauration sélective d'une seule table le jour où c'est ce qu'on veut. Il est
# déjà compressé : le gzip d'avant n'apportait rien.
#
# ─────────────────────────────────────────────────────────────────────────────────
# RESTAURATION — voir docs/RUNBOOK_Bascule_Base.md. En résumé :
#
#   psql -U postgres -f globals-<horodatage>.sql
#   pg_restore -U postgres -d HbaExpressDb --clean --if-exists --jobs 4 \
#              marketplace-<horodatage>.dump
# ═════════════════════════════════════════════════════════════════════════════════
set -euo pipefail

# Les dumps contiennent l'intégralité des données, et celui des rôles contient les
# hachages de mots de passe. Le répertoire est en 0700, mais un umask hérité de cron
# donnerait des fichiers en 0644 à l'intérieur : ceinture et bretelles, pour une ligne.
umask 077

CONFIG_FILE="${CONFIG_FILE:-/etc/marketplace-backup.env}"
# shellcheck disable=SC1090
[ -f "$CONFIG_FILE" ] && . "$CONFIG_FILE"

BACKUP_DIR="${BACKUP_DIR:-/var/backups/marketplace}"
LOCAL_RETENTION_DAYS="${LOCAL_RETENTION_DAYS:-7}"
BACKUP_BUCKET="${BACKUP_BUCKET:-hbaexpress-backups}"
BACKUP_PREFIX="${BACKUP_PREFIX:-postgres}"
PG_USER="${PG_USER:-postgres}"
# PAS de liste par défaut ici : ce serait une TROISIÈME écriture de l'identité des
# bases, après le group_vars et les defaults du rôle. C'est exactement la divergence
# qu'on cherche à supprimer. La configuration la fournit, ou le script s'arrête.
PG_DATABASES="${PG_DATABASES:-}"
STATUS_FILE="${STATUS_FILE:-$BACKUP_DIR/.derniere-reussite}"

# Seuil au-delà duquel `--check` considère la sauvegarde en défaut. Deux fois la
# période nominale : un cycle raté n'alerte pas, deux cycles ratés alertent.
MAX_AGE_HOURS="${MAX_AGE_HOURS:-13}"

log() { printf '%s  %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"; }
die() { printf '%s  ERREUR: %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >&2; exit 1; }

# ─────────────────────────────────────────────────────────────────────────────────
# MODE `--check` : la sauvegarde a-t-elle réellement eu lieu ?
#
# Une sauvegarde qu'on ne surveille pas est une sauvegarde qu'on découvre absente le
# jour où l'on en a besoin. Ce mode est fait pour être appelé par un cron distinct :
# il sort en code non nul si la dernière réussite est trop ancienne, ce qui suffit à
# faire parler cron par e-mail — sans dépendre d'une pile d'observabilité.
# ─────────────────────────────────────────────────────────────────────────────────
case "${1:-}" in
  ""|--check) ;;
  *) die "argument inconnu : $1 (seul --check est accepté)" ;;
esac

if [ "${1:-}" = "--check" ]; then
  [ -f "$STATUS_FILE" ] || die "aucune sauvegarde n'a JAMAIS réussi ($STATUS_FILE absent)"
  last="$(cat "$STATUS_FILE")"
  # Un marqueur vide ou corrompu — disque plein au moment de l'écrire — ferait
  # échouer l'arithmétique sur un message bash illisible. Le résultat serait bon
  # (cron alerte), la raison incompréhensible.
  case "$last" in ''|*[!0-9]*) die "marqueur de réussite illisible ($STATUS_FILE)" ;; esac
  age=$(( ( $(date -u +%s) - last ) / 3600 ))
  [ "$age" -le "$MAX_AGE_HOURS" ] \
    || die "dernière sauvegarde réussie il y a ${age} h (seuil : ${MAX_AGE_HOURS} h)"
  log "dernière sauvegarde réussie il y a ${age} h — conforme."
  exit 0
fi

command -v pg_dump >/dev/null || die "pg_dump absent — ce script tourne sur le VPS base"
[ -n "$PG_DATABASES" ] || die "PG_DATABASES vide dans $CONFIG_FILE — rien à sauvegarder"
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

STAMP="$(date -u +'%Y%m%d-%H%M%S')"
PRODUITS=()

# ── 1. Les rôles ─────────────────────────────────────────────────────────────────
GLOBALS="$BACKUP_DIR/globals-$STAMP.sql"
log "pg_dumpall --globals-only → $GLOBALS"
if ! pg_dumpall -U "$PG_USER" --globals-only > "$GLOBALS"; then
  rm -f "$GLOBALS"
  die "pg_dumpall a échoué"
fi
PRODUITS+=("$(basename "$GLOBALS")")

# ── 2. Les bases ─────────────────────────────────────────────────────────────────
for db in $PG_DATABASES; do
  DEST="$BACKUP_DIR/${db}-$STAMP.dump"
  log "pg_dump de « $db » → $DEST"

  # PAS de --no-owner ici, contrairement à la version précédente. Le propriétaire
  # et les droits sont l'information qui manquait le plus à la restauration : sans
  # eux il fallait rejouer les GRANT à la main, sur une base fraîchement restaurée,
  # un jour d'incident. `pg_restore` sait les ignorer si on le lui demande ; il ne
  # sait pas les inventer.
  if ! pg_dump -U "$PG_USER" -d "$db" --format=custom --compress=6 --file="$DEST"; then
    rm -f "$DEST"
    die "pg_dump de « $db » a échoué"
  fi

  # Un dump vide ou tronqué ne doit jamais être compté comme une sauvegarde.
  SIZE=$(stat -c%s "$DEST" 2>/dev/null || stat -f%z "$DEST")
  [ "${SIZE:-0}" -gt 1000 ] || die "dump de « $db » anormalement petit ($SIZE octets)"

  # Le dump se relit-il ? `pg_restore --list` parcourt la table des matières sans
  # rien écrire : c'est le contrôle d'intégrité le moins cher qui existe, et il
  # attrape le cas où le disque s'est rempli en cours d'écriture.
  pg_restore --list "$DEST" > /dev/null || die "dump de « $db » illisible"

  log "  OK — $SIZE octets, table des matières lisible"
  PRODUITS+=("$(basename "$DEST")")
done

# ── 3. Hors de la machine ────────────────────────────────────────────────────────
#
# Un dump qui reste sur le serveur qu'il sauvegarde ne protège de rien : ni de la
# perte du VPS, ni du chiffrement du disque, ni d'un `rm` malheureux. L'envoi
# distant EST la sauvegarde ; le fichier local n'en est que la copie de travail.
if [ -n "${R2_ACCOUNT_ID:-}" ] && [ -n "${R2_ACCESS_KEY_ID:-}" ] && [ -n "${R2_SECRET_ACCESS_KEY:-}" ]; then
  export AWS_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
  export AWS_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
  export AWS_DEFAULT_REGION=auto
  ENDPOINT="https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com"

  for f in "${PRODUITS[@]}"; do
    log "envoi → r2://$BACKUP_BUCKET/$BACKUP_PREFIX/$f"
    aws s3 cp "$BACKUP_DIR/$f" "s3://$BACKUP_BUCKET/$BACKUP_PREFIX/$f" \
      --endpoint-url "$ENDPOINT" --only-show-errors \
      || die "envoi de $f échoué (les dumps locaux sont conservés dans $BACKUP_DIR)"
  done
  log "envoi distant terminé"
else
  # Volontairement une ERREUR et non un avertissement. Une sauvegarde qui ne quitte
  # pas la machine sauvegardée n'est pas une sauvegarde, et le dire en jaune dans un
  # journal que personne ne lit revient à ne pas le dire.
  die "identifiants R2 absents de $CONFIG_FILE — la sauvegarde N'A PAS quitté la machine"
fi

# ── 4. Purge locale ──────────────────────────────────────────────────────────────
log "purge locale au-delà de ${LOCAL_RETENTION_DAYS} jours"
find "$BACKUP_DIR" -type f \( -name '*.dump' -o -name 'globals-*.sql' \) \
  -mtime "+$LOCAL_RETENTION_DAYS" -print -delete || true

# ── 5. Marqueur de réussite ──────────────────────────────────────────────────────
date -u +%s > "$STATUS_FILE"
log "terminé — ${#PRODUITS[@]} fichier(s) sauvegardés et envoyés."
