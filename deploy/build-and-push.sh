#!/usr/bin/env bash
# =====================================================================
#  Build LOCAL de l'image de l'hôte + push vers un registry, pour que le VPS ne
#  fasse QUE « pull » (aucune compilation sur le serveur).
#
#  La CI n'utilise PAS ce script : elle passe par docker/build-push-action pour
#  bénéficier du cache GitHub Actions. Les deux construisent le même Dockerfile
#  avec les mêmes arguments — s'ils changent, changer les deux
#  (.github/workflows/deploy.yml).
#
#  Le VPS est en linux/amd64 : on force cette plateforme (utile depuis un Mac
#  Apple Silicon arm64) via docker buildx.
#
#  Prérequis : docker buildx, et `docker login ghcr.io`.
#
#  Usage :
#    IMAGE_PREFIX=ghcr.io/mon-user IMAGE_TAG=staging ./deploy/build-and-push.sh
#
#  Variables :
#    IMAGE_PREFIX  obligatoire
#    IMAGE_TAG     tag mobile (défaut « staging »)
#    IMAGE_SHA     tag immuable (défaut : sha-<12 car. du commit courant>).
#                  IMAGE_SHA=- pour ne pousser que le tag mobile.
#    PLATFORM      défaut linux/amd64
# =====================================================================
set -euo pipefail

: "${IMAGE_PREFIX:?IMAGE_PREFIX requis (ex: ghcr.io/mon-user ou docker.io/mon-user)}"
IMAGE_TAG="${IMAGE_TAG:-staging}"
PLATFORM="${PLATFORM:-linux/amd64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

PROJECT="src/Host/Marketplace.Host/Marketplace.Host.csproj"
APP_DLL="Marketplace.Host.dll"

# DEUX ÉTIQUETTES, ET LA SECONDE EST CELLE QUI PERMET DE REVENIR EN ARRIÈRE.
# `latest`/`staging` bougent à chaque livraison : elles ne désignent pas un commit.
# `sha-…` est immuable, donc redéployable sans rebuild et sans revert.
if [ -z "${IMAGE_SHA:-}" ]; then
  IMAGE_SHA="sha-$(git -C "$ROOT" rev-parse HEAD 2>/dev/null | cut -c1-12)"
  [ "$IMAGE_SHA" = "sha-" ] && IMAGE_SHA="-"
fi

TAGS=(-t "${IMAGE_PREFIX}/api:${IMAGE_TAG}")
[ "$IMAGE_SHA" != "-" ] && TAGS+=(-t "${IMAGE_PREFIX}/api:${IMAGE_SHA}")

# Un builder buildx dédié (multi-plateforme).
docker buildx inspect mp-builder >/dev/null 2>&1 || docker buildx create --name mp-builder >/dev/null
docker buildx use mp-builder

echo "==> Build+push  ${IMAGE_PREFIX}/api   ($PLATFORM)"
docker buildx build \
  --platform "$PLATFORM" \
  -f "$ROOT/deploy/Dockerfile" \
  --build-arg PROJECT="$PROJECT" \
  --build-arg APP_DLL="$APP_DLL" \
  "${TAGS[@]}" \
  --push \
  "$ROOT"

echo ""
echo "OK — étiquettes poussées :"
for t in "${TAGS[@]}"; do [ "$t" != "-t" ] && echo "  $t"; done
echo ""
echo "Déploie ensuite sur le serveur (mode pull) :"
echo "  cd deploy/ansible && ansible-playbook -i inventories/staging/hosts.ini site.yml \\"
echo "    --tags deploy -e api_image_tag=${IMAGE_SHA}"
