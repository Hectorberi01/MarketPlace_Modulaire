#!/usr/bin/env bash
# =====================================================================
#  Build LOCAL des 5 images (API + 4 BFF) + push vers un registry, pour
#  que le VPS ne fasse QUE « pull » (aucune compilation sur le serveur).
#
#  Le VPS est en linux/amd64 : on force cette plateforme (utile depuis un
#  Mac Apple Silicon arm64) via docker buildx.
#
#  Prérequis :
#    - docker buildx (inclus dans Docker Desktop)
#    - être connecté au registry :   docker login ghcr.io   (ou docker.io)
#
#  Usage :
#    IMAGE_PREFIX=ghcr.io/mon-user IMAGE_TAG=staging ./deploy/build-and-push.sh
#    IMAGE_PREFIX=docker.io/mon-user IMAGE_TAG=staging ./deploy/build-and-push.sh
# =====================================================================
set -euo pipefail

: "${IMAGE_PREFIX:?IMAGE_PREFIX requis (ex: ghcr.io/mon-user ou docker.io/mon-user)}"
IMAGE_TAG="${IMAGE_TAG:-staging}"
PLATFORM="${PLATFORM:-linux/amd64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# service | projet .csproj | dll d'entrée
SERVICES="
api|src/Bootstrap/Marketplace.Api/Marketplace.Api.csproj|Marketplace.Api.dll
bff-client|src/Bff/Marketplace.Bff.Client/Marketplace.Bff.Client.csproj|Marketplace.Bff.Client.dll
bff-seller|src/Bff/Marketplace.Bff.Seller/Marketplace.Bff.Seller.csproj|Marketplace.Bff.Seller.dll
bff-admin|src/Bff/Marketplace.Bff.Admin/Marketplace.Bff.Admin.csproj|Marketplace.Bff.Admin.dll
bff-driver|src/Bff/Marketplace.Bff.Driver/Marketplace.Bff.Driver.csproj|Marketplace.Bff.Driver.dll
dispatch|Services/HbaExpress.Dispatch/src/HbaExpress.Dispatch.Api/HbaExpress.Dispatch.Api.csproj|HbaExpress.Dispatch.Api.dll
"

# Un builder buildx dédié (multi-plateforme).
docker buildx inspect mp-builder >/dev/null 2>&1 || docker buildx create --name mp-builder >/dev/null
docker buildx use mp-builder

echo "$SERVICES" | while IFS='|' read -r svc proj dll; do
  [ -z "${svc:-}" ] && continue
  img="${IMAGE_PREFIX}/${svc}:${IMAGE_TAG}"
  echo "==> Build+push  $img   ($PLATFORM)"
  docker buildx build \
    --platform "$PLATFORM" \
    -f "$ROOT/deploy/Dockerfile" \
    --build-arg PROJECT="$proj" \
    --build-arg APP_DLL="$dll" \
    -t "$img" \
    --push \
    "$ROOT"
done

echo ""
echo "OK — 6 images poussées : ${IMAGE_PREFIX}/{api,bff-client,bff-seller,bff-admin,bff-driver,dispatch}:${IMAGE_TAG}"
echo "Déploie ensuite sur le serveur (mode pull) :"
echo "  cd deploy/ansible && ansible-playbook -i inventories/staging/hosts.ini site.yml --tags deploy"
