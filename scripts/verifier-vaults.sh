#!/usr/bin/env bash
# Refuse un vault Ansible SUIVI PAR GIT qui n'est pas chiffré.
#
# .gitignore exempte nommément les vaults de staging et de prod, parce qu'un vault
# CHIFFRÉ doit être versionné. L'exemption ne vérifie rien : un `ansible-vault
# decrypt` oublié, et le fichier part en clair au commit suivant.
#
# Usage : scripts/verifier-vaults.sh   (depuis la racine du dépôt)
set -euo pipefail

EN_CLAIR=()

while IFS= read -r fichier; do
    [ -f "$fichier" ] || continue
    if ! head -c 14 "$fichier" | grep -q '^\$ANSIBLE_VAULT'; then
        EN_CLAIR+=("$fichier")
    fi
done < <(git ls-files '*/vault.yml' 'vault.yml')

if [ ${#EN_CLAIR[@]} -gt 0 ]; then
    echo "VAULT SUIVI PAR GIT ET NON CHIFFRÉ :" >&2
    for f in "${EN_CLAIR[@]}"; do echo "  $f" >&2; done
    echo >&2
    echo "Chiffrer avant de committer :  ansible-vault encrypt <fichier>" >&2
    echo "Un vault d'exemple se nomme vault.example.yml et reste en clair." >&2
    exit 1
fi

echo "Vaults suivis : tous chiffrés."
