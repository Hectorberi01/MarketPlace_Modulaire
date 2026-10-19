#!/usr/bin/env bash
# =====================================================================
#  Déploiement PRODUCTION — Marketplace
#
#  Enchaîne, en s'arrêtant à la première erreur :
#    1. build .NET (Release)      — garde-fou de compilation
#    2. tests .NET                — la prod ne part QUE si tout passe
#    3. ansible-playbook          — rsync + (build images sur l'hôte) + up
#
#  Le build/test .NET local est un VERROU : il valide que la solution compile
#  et que les tests passent AVANT de toucher au serveur. Les images Docker,
#  elles, sont construites sur l'hôte par le rôle Ansible « marketplace »
#  (ou tirées d'un registry si use_registry=true).
#
#  Usage :
#    deploy/deploy-prod.sh [options]
#
#  Options :
#    -y, --yes              Ne pas demander de confirmation (CI).
#        --first-run        Provisionne aussi Docker (tags: docker,setup,deploy).
#        --tags "a,b"       Force les tags Ansible (défaut: deploy).
#        --env NAME         Inventaire (défaut: prod). Ex: staging.
#        --limit HOST       Restreint à un hôte de l'inventaire.
#        --skip-build       Saute le build .NET (les tests rebuilderont si besoin).
#        --skip-tests       Saute les tests .NET (DÉCONSEILLÉ).
#        --check            Ansible en dry-run (--check --diff), ne modifie rien.
#        --vault-pass-file F  Fichier de mot de passe du vault (sinon: saisie interactive).
#    -h, --help             Cette aide.
#
#  Variables d'env respectées :
#    ANSIBLE_VAULT_PASSWORD_FILE   fichier vault (équivaut à --vault-pass-file)
#    DOTNET_TEST_ARGS              args additionnels passés à `dotnet test`
# =====================================================================
set -euo pipefail

# ----------------------------- réglages ------------------------------
ENV_NAME="prod"
TAGS="deploy"
RUN_BUILD=1
RUN_TESTS=1
ASSUME_YES=0
ANSIBLE_CHECK=0
LIMIT=""
CONFIG="Release"
VAULT_PASS_FILE="${ANSIBLE_VAULT_PASSWORD_FILE:-}"

# ----------------------------- couleurs ------------------------------
if [ -t 1 ]; then
  BOLD=$'\033[1m'; RED=$'\033[31m'; GRN=$'\033[32m'; YEL=$'\033[33m'; BLU=$'\033[34m'; RST=$'\033[0m'
else
  BOLD=""; RED=""; GRN=""; YEL=""; BLU=""; RST=""
fi
step() { printf '\n%s==> %s%s\n' "$BOLD$BLU" "$*" "$RST"; }
ok()   { printf '%s✓ %s%s\n' "$GRN" "$*" "$RST"; }
warn() { printf '%sIMPORTANT : %s%s\n' "$YEL" "$*" "$RST"; }
die()  { printf '%s✗ %s%s\n' "$RED" "$*" "$RST" >&2; exit 1; }

# --------------------------- parse options ---------------------------
while [ $# -gt 0 ]; do
  case "$1" in
    -y|--yes)          ASSUME_YES=1 ;;
    --first-run)       TAGS="docker,setup,deploy" ;;
    --tags)            TAGS="${2:?--tags requiert une valeur}"; shift ;;
    --env)             ENV_NAME="${2:?--env requiert une valeur}"; shift ;;
    --limit)           LIMIT="${2:?--limit requiert une valeur}"; shift ;;
    --skip-build)      RUN_BUILD=0 ;;
    --skip-tests)      RUN_TESTS=0 ;;
    --check)           ANSIBLE_CHECK=1 ;;
    --vault-pass-file) VAULT_PASS_FILE="${2:?--vault-pass-file requiert un chemin}"; shift ;;
    -h|--help)         sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)                 die "Option inconnue : $1  (voir --help)" ;;
  esac
  shift
done

# ----------------------------- chemins -------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SLN="$REPO_ROOT/Marketplace.sln"
ANSIBLE_DIR="$REPO_ROOT/deploy/ansible"
INVENTORY="$ANSIBLE_DIR/inventories/$ENV_NAME/hosts.ini"
# Les deux inventaires ne rangent pas leur vault au même endroit : prod sous
# group_vars/all/ (le groupe `database` doit le lire aussi), staging sous
# group_vars/marketplace/. On prend celui qui existe.
VAULT=""
for candidat in \
  "$ANSIBLE_DIR/inventories/$ENV_NAME/group_vars/all/vault.yml" \
  "$ANSIBLE_DIR/inventories/$ENV_NAME/group_vars/marketplace/vault.yml"; do
  [ -f "$candidat" ] && { VAULT="$candidat"; break; }
done
# Aucun des deux : on nomme le chemin attendu pour le message d'erreur plus bas.
[ -n "$VAULT" ] || VAULT="$ANSIBLE_DIR/inventories/$ENV_NAME/group_vars/all/vault.yml"

# --------------------------- pré-vérifs ------------------------------
step "Pré-vérifications ($ENV_NAME)"

[ -f "$SLN" ]       || die "Solution introuvable : $SLN"
[ -f "$INVENTORY" ] || die "Inventaire introuvable : $INVENTORY"

command -v ansible-playbook >/dev/null || die "ansible-playbook absent (pip install ansible)."
command -v ansible-galaxy   >/dev/null || die "ansible-galaxy absent."
if [ "$RUN_BUILD" = 1 ] || [ "$RUN_TESTS" = 1 ]; then
  command -v dotnet >/dev/null || die "dotnet absent (SDK .NET 8 requis)."
fi

# Inventaire réellement renseigné (pas le gabarit).
if grep -q "CHANGER_IP_OU_HOST" "$INVENTORY"; then
  die "L'inventaire $ENV_NAME contient encore « CHANGER_IP_OU_HOST ». Renseigne l'IP/host du serveur."
fi
TARGET_HOST="$(awk '/ansible_host=/{for(i=1;i<=NF;i++) if($i ~ /^ansible_host=/){sub(/^ansible_host=/,"",$i); print $i}}' "$INVENTORY" | head -n1)"
[ -n "${TARGET_HOST:-}" ] || warn "Impossible de lire ansible_host dans l'inventaire (l'exécution continuera)."

# Vault : présent ET chiffré (jamais de secrets en clair).
[ -f "$VAULT" ] || die "Vault absent : $VAULT
  → cp $ANSIBLE_DIR/inventories/$ENV_NAME/vault.example.yml $VAULT
    ansible-vault encrypt $VAULT"
if ! head -n1 "$VAULT" | grep -q '^\$ANSIBLE_VAULT'; then
  die "Le vault n'est PAS chiffré ($VAULT). Chiffre-le : ansible-vault encrypt \"$VAULT\""
fi
ok "Solution, inventaire et vault (chiffré) présents."

# Traçabilité git (non bloquant).
if command -v git >/dev/null && git -C "$REPO_ROOT" rev-parse >/dev/null 2>&1; then
  GIT_SHA="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo '?')"
  GIT_BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
  if ! git -C "$REPO_ROOT" diff --quiet || ! git -C "$REPO_ROOT" diff --cached --quiet; then
    warn "Arbre git NON PROPRE : le déploiement rsync l'état ACTUEL du disque (commits non requis)."
    GIT_DIRTY=" (dirty)"
  else
    GIT_DIRTY=""
  fi
  ok "Commit : $GIT_BRANCH@$GIT_SHA$GIT_DIRTY"
fi

# --------------------------- confirmation ----------------------------
if [ "$ASSUME_YES" != 1 ] && [ "$ANSIBLE_CHECK" != 1 ]; then
  # Majuscules via `tr` (portable) : la syntaxe ${VAR^^} exige bash 4+, or macOS
  # est encore en bash 3.2 par défaut → « bad substitution ».
  ENV_UPPER="$(printf '%s' "$ENV_NAME" | tr '[:lower:]' '[:upper:]')"
  printf '\n%sDéploiement %s%s%s vers %s%s%s — tags: %s%s%s\n' \
    "$BOLD" "$RED" "$ENV_UPPER" "$RST$BOLD" "$YEL" "${TARGET_HOST:-?}" "$RST$BOLD" "$YEL" "$TAGS" "$RST"
  printf 'Tape %s%s%s pour confirmer : ' "$BOLD" "$ENV_NAME" "$RST"
  read -r CONFIRM
  [ "$CONFIRM" = "$ENV_NAME" ] || die "Confirmation invalide — abandon."
fi

# ------------------------------ build --------------------------------
if [ "$RUN_BUILD" = 1 ]; then
  step "Build .NET ($CONFIG)"
  dotnet restore "$SLN"
  dotnet build "$SLN" -c "$CONFIG" --no-restore
  ok "Build OK."
else
  warn "Build sauté (--skip-build)."
fi

# ------------------------------ tests --------------------------------
if [ "$RUN_TESTS" = 1 ]; then
  step "Tests .NET"
  # Réutilise le build si on vient de le faire ; sinon `dotnet test` recompile.
  TEST_ARGS=(-c "$CONFIG")
  [ "$RUN_BUILD" = 1 ] && TEST_ARGS+=(--no-build --no-restore)
  # shellcheck disable=SC2086
  dotnet test "$SLN" "${TEST_ARGS[@]}" ${DOTNET_TEST_ARGS:-}
  ok "Tests OK."
else
  warn "Tests sautés (--skip-tests) — À N'UTILISER QU'EN CONNAISSANCE DE CAUSE."
fi

# ------------------------------ ansible ------------------------------
step "Déploiement Ansible ($ENV_NAME → ${TARGET_HOST:-?})"
cd "$ANSIBLE_DIR"

# Collections/rôles requis (idempotent).
[ -f requirements.yml ] && ansible-galaxy install -r requirements.yml

# Arguments dynamiques.
PLAY_ARGS=(-i "inventories/$ENV_NAME/hosts.ini" site.yml --tags "$TAGS")
[ -n "$LIMIT" ] && PLAY_ARGS+=(--limit "$LIMIT")
[ "$ANSIBLE_CHECK" = 1 ] && PLAY_ARGS+=(--check --diff)

# Vault : fichier de mot de passe si fourni, sinon saisie interactive.
if [ -n "$VAULT_PASS_FILE" ]; then
  [ -f "$VAULT_PASS_FILE" ] || die "Fichier vault-pass introuvable : $VAULT_PASS_FILE"
  PLAY_ARGS+=(--vault-password-file "$VAULT_PASS_FILE")
else
  PLAY_ARGS+=(--ask-vault-pass)
fi

ansible-playbook "${PLAY_ARGS[@]}"

step "Terminé"
ok "Déploiement $ENV_NAME appliqué sur ${TARGET_HOST:-?}."
[ "$ANSIBLE_CHECK" = 1 ] && warn "(mode --check : aucune modification réelle n'a été appliquée)"
