#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
═══════════════════════════════════════════════════════════════════════════════════
PUBLIE LES QUATRE TEXTES CONTRACTUELS DANS LE CMS, PAR L'API D'ADMINISTRATION.

CE QUE ÇA DÉBLOQUE. Tant qu'aucun bloc n'est publié, `GET /seller/legal/terms` répond
404 — le comportement prévu — et les portes de consentement du WEB, qui n'embarquent
aucune copie de repli, ne s'affichent jamais. Un vendeur inscrit depuis le navigateur
n'accepte donc aucune condition, là où celui qui s'inscrit depuis le téléphone en
accepte. Publier ces quatre blocs ferme cet écart et fait taire les 404 du même geste.

═══════════════════════════════════════════════════════════════════════════════════
IL NE PUBLIE RIEN SANS `--publier`, ET C'EST LE POINT LE PLUS IMPORTANT DE CE FICHIER.

Par défaut il SIMULE : il dit ce qu'il ferait, emplacement par emplacement, et sort.
Ce qui part ici devient le texte opposable aux vendeurs et aux acheteurs de la
plateforme ; un script qui écrit au premier lancement serait une façon de publier un
contrat sans l'avoir décidé.

RELISEZ `documents/*.json` AVANT. Ils sont produits par `extraire_textes_legaux.py` à
partir des textes des applications mobiles — ceux que les utilisateurs mobiles
acceptent déjà. Ce script ne les relit pas de la source : il publie les fichiers tels
qu'ils sont sur le disque, donc vos corrections éventuelles partent avec.
═══════════════════════════════════════════════════════════════════════════════════

══════════════════════════════════════════════════════════════════════════════════
L'ADRESSE EST CELLE DU BFF ADMIN, PAS CELLE DE LA CONSOLE.

La console web expose `/api/bff/…`, mais sa passerelle s'authentifie par le COOKIE de
session chiffré : elle ignore un en-tête `Authorization` et répondrait « Non
authentifié ». Ce script parle donc au BFF lui-même, avec un jeton porteur.

  depuis le VPS .......... http://bff-admin:8080      (réseau Docker interne)
  depuis l'extérieur ..... https://admin.<domaine>    (route Traefik du service)
══════════════════════════════════════════════════════════════════════════════════

Usage :

    export API_BASE_URL=http://api:8080
    export ADMIN_TOKEN=<jeton JWT d'un compte administrateur>

    python3 publier_textes_legaux.py                # simulation, n'écrit rien
    python3 publier_textes_legaux.py --publier      # publie ce qui manque
    python3 publier_textes_legaux.py --publier --remplacer   # archive l'ancien d'abord

Le jeton n'est ni journalisé ni affiché : il vit dans l'en-tête et nulle part ailleurs.
"""

import json
import os
import sys
import urllib.error
import urllib.request

RACINE = os.path.dirname(os.path.abspath(__file__))
DOSSIER = os.path.join(RACINE, "documents")

# L'ordre n'a pas d'importance fonctionnelle, mais il rend la sortie lisible : les deux
# documents d'une même audience se suivent.
EMPLACEMENTS = [
    "legal_terms_seller",
    "legal_privacy_seller",
    "legal_terms_customer",
    "legal_privacy_customer",
]

# « Page » parmi Banner, Page, FeaturedList, Faq. Un document contractuel est une page :
# les trois autres types portent un rendu (carrousel, liste, accordéon) qui n'a rien à
# voir avec un texte qu'on fait défiler jusqu'au bout avant d'accepter.
TYPE_BLOC = "Page"


class Echec(Exception):
    pass


def appeler(methode, chemin, jeton, base, charge=None):
    url = base.rstrip("/") + chemin
    corps = None if charge is None else json.dumps(charge).encode("utf-8")

    requete = urllib.request.Request(url, data=corps, method=methode)
    requete.add_header("Authorization", "Bearer " + jeton)
    if corps is not None:
        requete.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(requete, timeout=30) as reponse:
            brut = reponse.read().decode("utf-8")
            return json.loads(brut) if brut.strip() else None
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")[:400]
        # Le jeton n'apparaît jamais dans ce message : seul le chemin et la réponse.
        raise Echec(f"{methode} {chemin} → {e.code}. {detail}") from None
    except urllib.error.URLError as e:
        raise Echec(f"{methode} {chemin} injoignable : {e.reason}") from None


def documents_existants(emplacement, jeton, base):
    """Les blocs déjà présents sur cet emplacement, tous statuts confondus.

    `/admin/content/slot/{slot}` rend les blocs PUBLIÉS. Un brouillon laissé là par la
    console n'apparaît donc pas — et republier par-dessus créerait un doublon invisible.
    On passe donc par la liste complète, qui porte le statut de chacun.
    """
    tous = appeler("GET", "/admin/content", jeton, base) or []
    return [b for b in tous if b.get("slot") == emplacement]


def version_du_bloc(bloc):
    """La version que porte le contenu d'un bloc, ou None s'il est illisible."""
    try:
        return (json.loads(bloc.get("content") or "{}") or {}).get("version")
    except (json.JSONDecodeError, TypeError):
        return None


def main():
    publier = "--publier" in sys.argv
    remplacer = "--remplacer" in sys.argv

    # ADMIN_BFF_URL reste lu en repli : l'hôte unique a remplacé les BFF.
    base = os.environ.get("API_BASE_URL") or os.environ.get("ADMIN_BFF_URL")
    jeton = os.environ.get("ADMIN_TOKEN")
    if not base or not jeton:
        raise Echec("API_BASE_URL et ADMIN_TOKEN sont requis. Voir l'en-tête.")

    if not publier:
        print("SIMULATION — rien ne sera écrit. Ajoutez --publier pour agir.\n")

    agis = 0
    for emplacement in EMPLACEMENTS:
        fichier = os.path.join(DOSSIER, emplacement + ".json")
        if not os.path.exists(fichier):
            raise Echec(f"{fichier} absent. Lancez extraire_textes_legaux.py d'abord.")

        document = json.load(open(fichier, encoding="utf-8"))
        version = document.get("version")
        if not version or not document.get("sections"):
            # Le serveur refuserait le bloc et rendrait 404 : on l'arrête ici, où le
            # message peut encore dire lequel des quatre est en cause.
            raise Echec(f"{emplacement} : sans `version` ou sans `sections`, ce bloc "
                        f"serait publié puis ignoré par le serveur.")

        existants = documents_existants(emplacement, jeton, base)
        publies = [b for b in existants if b.get("status") == "Published"]
        deja = [b for b in publies if version_du_bloc(b) == version]

        if deja:
            print(f"{emplacement:<24} déjà publié en version {version} — rien à faire.")
            continue

        if publies and not remplacer:
            autres = ", ".join(str(version_du_bloc(b)) for b in publies)
            print(f"{emplacement:<24} DÉJÀ PUBLIÉ en version {autres}, et vous publiez "
                  f"{version}.\n{'':<24} Relancez avec --remplacer pour archiver "
                  f"l'ancien. REFUSÉ ici : écraser un texte accepté par des "
                  f"utilisateurs n'est pas une opération de routine.")
            continue

        signes = sum(len(p) for s in document["sections"] for p in s["body"])
        action = "publierait" if not publier else "publie"
        print(f"{emplacement:<24} {action} la version {version} "
              f"({len(document['sections'])} sections, {signes} signes)"
              + (f", après archivage de {len(publies)} bloc(s)" if publies else ""))

        if not publier:
            agis += 1
            continue

        for ancien in publies:
            appeler("POST", f"/admin/content/{ancien['id']}/archive", jeton, base)
            print(f"{'':<24}   archivé {ancien['id']}")

        cree = appeler("POST", "/admin/content", jeton, base, {
            "type": TYPE_BLOC,
            "slot": emplacement,
            "position": 0,
            # Le CONTENU est le document sérialisé : c'est ce que
            # `GetLegalDocumentQueryHandler` désérialise à la lecture. `ensure_ascii`
            # à faux garde les accents lisibles dans le back-office.
            "content": json.dumps(document, ensure_ascii=False),
            "startUtc": None,
            "endUtc": None,
        })
        identifiant = (cree or {}).get("contentBlockId")
        if not identifiant:
            raise Echec(f"{emplacement} : création sans identifiant en retour.")

        # CRÉÉ EN BROUILLON, PUIS PUBLIÉ : deux appels, et le second peut échouer. Un
        # brouillon resté seul ne sert AUCUN texte — le 404 d'avant persiste — mais il
        # ne sert pas non plus un texte à moitié. C'est le bon sens de l'échec.
        appeler("POST", f"/admin/content/{identifiant}/publish", jeton, base)
        print(f"{'':<24}   publié {identifiant}")
        agis += 1

    print()
    if not publier:
        print(f"{agis} emplacement(s) seraient modifiés. Relisez documents/*.json, "
              f"puis relancez avec --publier.")
    else:
        print(f"{agis} emplacement(s) publiés. Vérifiez : les 404 de "
              f"/seller/legal/terms doivent disparaître, et la porte de consentement "
              f"s'afficher à la prochaine ouverture de la console.")


if __name__ == "__main__":
    try:
        main()
    except Echec as e:
        print(f"Interrompu : {e}", file=sys.stderr)
        sys.exit(1)
