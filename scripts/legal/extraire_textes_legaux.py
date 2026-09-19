#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
═══════════════════════════════════════════════════════════════════════════════════
DES TEXTES LÉGAUX DART VERS LE JSON QUE LE CMS ATTEND.

Les quatre documents contractuels de la plateforme ne sont écrits qu'à UN endroit :
`hba-express-mobile/lib/features/legal/{seller,customer}/legal_content.dart`. Ce sont
eux que les applications mobiles font accepter aujourd'hui, et le CMS n'en a aucun —
d'où le 404 de `/seller/legal/terms` et la porte de consentement qui ne s'ouvre jamais
sur le web.

CE SCRIPT NE PUBLIE RIEN. Il lit le Dart et écrit quatre fichiers JSON, à RELIRE avant
de lancer `publier_textes_legaux.py`. La séparation est délibérée : ce qui part au CMS
devient le texte opposable aux vendeurs et aux acheteurs, et personne ne doit pouvoir
le publier sans l'avoir eu sous les yeux.

POURQUOI UN LECTEUR DE CHAÎNES DART, ET PAS UNE EXPRESSION RÉGULIÈRE.

Une regex sur `"(.*?)"` coupe au premier guillemet échappé et avale une virgule de
séparation comme du texte. Sur un contrat, l'erreur ne se voit pas : on obtient un
paragraphe amputé, parfaitement lisible, qui dit autre chose. Le lecteur ci-dessous
parcourt les caractères un par un, comme le ferait le compilateur — il connaît les
échappements, les deux familles de guillemets, la concaténation de littéraux adjacents
et l'interpolation `$nom`.

CE QU'IL REFUSE PLUTÔT QUE DE DEVINER : une interpolation dont il ne connaît pas la
constante, une expression `${...}` autre qu'un simple identifiant, un échappement qu'il
ne sait pas lire. Il lève. Un contrat où il manque un montant n'est pas un contrat.
═══════════════════════════════════════════════════════════════════════════════════
"""

import json
import os
import re
import sys

RACINE = os.path.dirname(os.path.abspath(__file__))
MOBILE = os.path.abspath(
    os.path.join(RACINE, "..", "..", "..", "hba-express-mobile",
                 "lib", "features", "legal"))

# Chaque document : son fichier source, la constante Dart qui le porte, et l'emplacement
# CMS où il doit atterrir. Les emplacements sont ceux que les BFF interrogent —
# SellerLegalEndpoints et ClientContentEndpoints ; les recopier ailleurs ferait diverger.
DOCUMENTS = [
    ("seller/legal_content.dart", "terms", "legal_terms_seller"),
    ("seller/legal_content.dart", "privacy", "legal_privacy_seller"),
    ("customer/legal_content.dart", "terms", "legal_terms_customer"),
    ("customer/legal_content.dart", "privacy", "legal_privacy_customer"),
]

ECHAPPEMENTS = {"n": "\n", "t": "\t", "r": "\r", "'": "'", '"': '"',
                "\\": "\\", "$": "$"}


def sauter(source, i, aussi=""):
    """Avance sur les blancs, les caractères de `aussi`, ET LES COMMENTAIRES.

    ═══════════════════════════════════════════════════════════════════════════════
    LES COMMENTAIRES VIVENT AU MILIEU DES PARAGRAPHES, ET CE N'EST PAS UN ACCIDENT.

    Ces fichiers portent l'historique de leurs corrections là où elle se lit : un bloc
    `//` entre deux paragraphes explique pourquoi le suivant a été ajouté. Une première
    version de ce lecteur s'est arrêtée dessus — et c'est tant mieux, parce que
    l'alternative silencieuse aurait été de prendre `// PRESTATAIRE OMIS…` pour un
    paragraphe du contrat et de le publier tel quel.
    ═══════════════════════════════════════════════════════════════════════════════
    """
    while i < len(source):
        c = source[i]
        if c in " \t\r\n" or c in aussi:
            i += 1
        elif source.startswith("//", i):
            fin = source.find("\n", i)
            i = len(source) if fin < 0 else fin + 1
        elif source.startswith("/*", i):
            fin = source.find("*/", i)
            if fin < 0:
                raise LectureImpossible("commentaire de bloc jamais refermé")
            i = fin + 2
        else:
            break
    return i


class LectureImpossible(Exception):
    """Levée dès qu'il faudrait deviner. Voir l'en-tête."""


def constantes(source):
    """Les `static const nom = 'valeur';` ou `= 12;` scalaires du fichier.

    Elles servent à résoudre les interpolations `$company`, `$commissionPercent`…
    Seules les valeurs SCALAIRES sont retenues : une liste n'a rien à faire dans une
    phrase, et l'y insérer produirait du Dart affiché tel quel à un lecteur.
    """
    valeurs = {}
    for m in re.finditer(
            r"static const (\w+)\s*=\s*('(?:[^'\\]|\\.)*'|\"(?:[^\"\\]|\\.)*\"|-?\d+)\s*;",
            source):
        nom, brut = m.group(1), m.group(2)
        valeurs[nom] = brut if brut[0].isdigit() or brut[0] == "-" else lire_litteral(brut, 0, {})[0]
    return valeurs


def lire_litteral(source, i, consts):
    """Lit UNE chaîne Dart à partir de `source[i]`. Rend (texte, index après la chaîne)."""
    guillemet = source[i]
    if guillemet not in "'\"":
        raise LectureImpossible(f"chaîne attendue à l'offset {i}, trouvé {source[i]!r}")

    i += 1
    morceaux = []
    while True:
        if i >= len(source):
            raise LectureImpossible("fin de fichier au milieu d'une chaîne")
        c = source[i]

        if c == "\\":
            suite = source[i + 1]
            if suite not in ECHAPPEMENTS:
                # `\u`, `\x`… : lisibles, mais aucun n'est utilisé dans ces fichiers.
                # Les traiter « au mieux » risquerait d'altérer un texte contractuel.
                raise LectureImpossible(f"échappement inconnu \\{suite} à l'offset {i}")
            morceaux.append(ECHAPPEMENTS[suite])
            i += 2
            continue

        if c == guillemet:
            i += 1
            break

        if c == "$":
            nom, i = lire_interpolation(source, i, consts)
            morceaux.append(nom)
            continue

        morceaux.append(c)
        i += 1

    # CONCATÉNATION DE LITTÉRAUX ADJACENTS : en Dart, deux chaînes séparées par des
    # blancs n'en font qu'une. Ne pas la suivre couperait un paragraphe en deux, et le
    # second morceau serait pris pour un paragraphe à part.
    j = sauter(source, i)
    if j < len(source) and source[j] in "'\"":
        suite, i = lire_litteral(source, j, consts)
        return "".join(morceaux) + suite, i

    return "".join(morceaux), i


def lire_interpolation(source, i, consts):
    """Résout `$nom` ou `${nom}`. Lève sur toute expression plus riche."""
    if source[i + 1] == "{":
        fin = source.index("}", i)
        nom = source[i + 2:fin].strip()
        apres = fin + 1
    else:
        m = re.match(r"\$(\w+)", source[i:])
        if not m:
            raise LectureImpossible(f"interpolation illisible à l'offset {i}")
        nom, apres = m.group(1), i + m.end()

    if not nom.isidentifier():
        raise LectureImpossible(f"expression interpolée non triviale : {nom!r}")
    if nom not in consts:
        raise LectureImpossible(
            f"constante `{nom}` introuvable — le texte publié porterait un trou")
    return str(consts[nom]), apres


def lire_sections(source, nom_constante, consts):
    """Les `LegalSection('titre', [ "corps", … ])` de la liste `nom_constante`."""
    debut = source.find(f"static const {nom_constante} = <LegalSection>[")
    if debut < 0:
        raise LectureImpossible(f"liste `{nom_constante}` introuvable")

    sections = []
    i = debut
    # La liste se termine au premier `];` en début de ligne — les listes de corps, elles,
    # sont indentées et fermées par `]),`.
    fin = source.index("\n  ];", debut)

    while True:
        depart = source.find("LegalSection(", i)
        if depart < 0 or depart > fin:
            break
        j = sauter(source, depart + len("LegalSection("))

        titre, j = lire_litteral(source, j, consts)

        j = source.index("[", j) + 1
        corps = []
        while True:
            j = sauter(source, j, aussi=",")
            if source[j] == "]":
                j += 1
                break
            paragraphe, j = lire_litteral(source, j, consts)
            corps.append(paragraphe)

        if not corps:
            raise LectureImpossible(f"section « {titre} » sans aucun paragraphe")

        sections.append({"title": titre, "body": corps})
        i = j

    if not sections:
        raise LectureImpossible(f"aucune section lue dans `{nom_constante}`")
    return sections


def main():
    sortie = os.path.join(RACINE, "documents")
    os.makedirs(sortie, exist_ok=True)

    total = 0
    for fichier, constante, emplacement in DOCUMENTS:
        chemin = os.path.join(MOBILE, fichier)
        source = open(chemin, encoding="utf-8").read()
        consts = constantes(source)

        for requis in ("version", "lastUpdated", "company"):
            if requis not in consts:
                raise LectureImpossible(f"{fichier} : `{requis}` manquant")

        document = {
            # Les trois en-têtes que `GetLegalDocumentQueryHandler` exige : sans version
            # ni sections, il refuse le bloc et rend 404 — donc rien n'aurait changé.
            "version": consts["version"],
            "lastUpdated": consts["lastUpdated"],
            "company": consts["company"],
            "sections": lire_sections(source, constante, consts),
        }

        cible = os.path.join(sortie, f"{emplacement}.json")
        with open(cible, "w", encoding="utf-8") as f:
            json.dump(document, f, ensure_ascii=False, indent=2)
            f.write("\n")

        signes = sum(len(p) for s in document["sections"] for p in s["body"])
        print(f"{emplacement:<24} {len(document['sections']):>2} sections, "
              f"{signes:>6} signes, version {document['version']}")
        total += signes

    print(f"\n{len(DOCUMENTS)} documents écrits dans {sortie} ({total} signes).")
    print("RELISEZ-LES avant de lancer publier_textes_legaux.py.")


if __name__ == "__main__":
    try:
        main()
    except LectureImpossible as e:
        print(f"Lecture interrompue : {e}", file=sys.stderr)
        print("Rien n'a été écrit. Corrigez la source ou ce lecteur — ne publiez pas "
              "un texte partiel.", file=sys.stderr)
        sys.exit(1)
