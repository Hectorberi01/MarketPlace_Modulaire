#!/usr/bin/env bash
# Tout module qui a un DbContext doit avoir ses migrations EF versionnées.
#
# Sans elles, `MigrateAsync` au démarrage ne crée RIEN et ne signale rien : le module
# démarre, puis échoue à la première requête sur « relation ... does not exist ». Son
# processeur d'outbox, lui, rejoue l'erreur toutes les cinq secondes.
#
# SANS --startup-project : chaque projet Infrastructure porte sa propre
# IDesignTimeDbContextFactory, donc un projet = un seul DbContext. Viser l'hôte
# ferait découvrir les 26 contextes d'un coup — « More than one DbContext was found ».
#
# Usage : scripts/verifier-migrations.sh   (depuis la racine du dépôt)
set -euo pipefail

SANS_MIGRATION=()

for projet in src/Modules/*/Marketplace.Modules.*.Infrastructure; do
    [ -d "$projet" ] || continue

    # Un DbContext, et pas une fabrique de conception (IDesignTimeDbContextFactory).
    contexte=$(find "$projet" -name "*DbContext.cs" -not -path "*/obj/*" -not -path "*/bin/*" \
                              -not -name "*Factory*" -print -quit)
    [ -n "$contexte" ] || continue

    if [ ! -d "$projet/Migrations" ] || [ -z "$(ls -A "$projet/Migrations" 2>/dev/null)" ]; then
        SANS_MIGRATION+=("$projet")
    fi
done

if [ ${#SANS_MIGRATION[@]} -gt 0 ]; then
    echo "MODULE AVEC UN DbContext ET SANS MIGRATION :" >&2
    for p in "${SANS_MIGRATION[@]}"; do
        module=$(basename "$(dirname "$p")")
        echo "  $p" >&2
        echo "    dotnet ef migrations add Initial${module} -p $p" >&2
    done
    exit 1
fi

echo "Migrations : chaque module ayant un DbContext a les siennes."
