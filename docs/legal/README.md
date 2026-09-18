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
| `terms_seller.json` | `legal_terms_seller` | `GET /seller/legal/terms` |
| `privacy_seller.json` | `legal_privacy_seller` | `GET /seller/legal/privacy` |

Quatre documents et non deux : le fond juridique est commun, la rédaction ne l'est pas.
Les textes acheteur énoncent ses DROITS, les textes vendeur ses OBLIGATIONS. Servir le
même bloc aux deux surfaces ferait accepter à chacun un texte écrit pour l'autre.

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

## À CORRIGER AVANT DE PUBLIER LES CONDITIONS VENDEUR

`terms_seller.json`, section « 3. Commission et frais », contient cette phrase :

> Vous fixez votre prix NET — ce que vous percevez. La plateforme ajoute une commission
> de 10 % et des frais de paiement de 5 % pour obtenir le prix payé par l'acheteur.

**Ce n'est plus le modèle en vigueur.** La bascule vers 2 % de frais de plateforme
calculés sur la commande a mis ces deux taux à zéro — `GET /seller/pricing` rend
aujourd'hui deux zéros. Publier ce texte tel quel mettrait dans un document contractuel
une tarification que la plateforme n'applique pas.

C'est une phrase à réécrire, pas un champ à corriger : elle décrit un mécanisme (prix net
du vendeur + majorations) qui a changé de nature. Le texte extrait est laissé **tel quel**
plutôt que réécrit de mon initiative — une clause tarifaire ne se reformule pas sans vous.

## Avant la première publication

Ces fichiers sont une transcription fidèle d'un texte existant, pas une relecture
juridique. Le fond n'a été ni vérifié ni mis à jour ; l'en-tête de `legal_content.dart`
signale d'ailleurs deux points à faire trancher par un juriste béninois côté vendeur.

## Ce qui reste à faire

L'application acheteur lit ces documents. Le portail web `Seller_mobile_Portail` sert
encore sa propre copie. Tant qu'il n'a pas basculé, la version commune reste une
convention tenue à la main.

L'analyse du bloc, elle, n'existe **qu'à un seul endroit** : `GetLegalDocumentQuery`,
dans le module Contenu. Les deux BFF ne décident plus que d'une chose — quels documents
leur surface expose, et sous quel nom.
