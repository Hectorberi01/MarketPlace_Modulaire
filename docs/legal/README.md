# Textes légaux servis par le serveur

## Ce que ces deux fichiers sont

Le contenu à publier dans le CMS pour que `GET /client/legal/{document}` réponde.
Ils ont été extraits **mot pour mot** de `legal_content.dart` de l'application
acheteur — chaque paragraphe a été recontrôlé contre la source — et portent la
version en vigueur au moment de l'extraction, `2026-07-12`.

| Fichier | Slot CMS | Route |
|---|---|---|
| `terms_customer.json` | `legal_terms_customer` | `GET /client/legal/terms` |
| `privacy_customer.json` | `legal_privacy_customer` | `GET /client/legal/privacy` |

## Comment les publier

Un **seul bloc** par document, de type `Page`, position `0`, dont le contenu est le
fichier JSON entier. Le publier le rend actif ; tant qu'il ne l'est pas, la route
répond 404 et l'application affiche la copie qu'elle embarque. **Rien ne casse avant
publication** — c'est le comportement prévu, pas une dégradation.

Découper un document en plusieurs blocs n'est pas prévu : le handler ne lit que le
premier par position. Un texte contractuel n'est pas une liste de fragments qu'on
assemble, sinon on peut en publier la moitié.

## La règle à ne pas perdre

`version` est ce que le serveur enregistre quand l'acheteur accepte. Elle vit dans le
même bloc que le texte, et c'est tout l'intérêt : elle ne peut pas désigner une autre
rédaction que celle qui l'accompagne.

**Toute modification de fond change la version**, et tout le parc installé redemande
l'accord à la session suivante — sans passer par les stores. Une correction de pure
forme (coquille, ponctuation) peut garder la version : c'est une décision, pas une
mécanique, et elle vous appartient.

## Avant la première publication

Ces fichiers sont une transcription fidèle d'un texte existant, pas une relecture
juridique. Le fond n'a été ni vérifié ni mis à jour ; l'en-tête de `legal_content.dart`
signale d'ailleurs deux points à faire trancher par un juriste béninois côté vendeur.

## Ce qui reste à faire

L'application acheteur lit ces documents. **L'application vendeur et le portail web
servent encore leur propre copie**, avec leur propre constante de version — quatre
fichiers Dart et un fichier TypeScript portaient la même chaîne `2026-07-12` recopiée
à la main. Tant qu'ils n'ont pas basculé, la version commune reste une convention
tenue par personne.
