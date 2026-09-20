# Conditions et confidentialité du LIVREUR — mécanisme et proposition de texte

> Rédigé le 20 septembre 2026, en même temps que le lot « consentement livreur ».
> Deux parties : **le mécanisme** (ce que le code fait désormais, et ce qu'il garantit),
> puis **le texte** (ce qui est proposé, et les six points qui restent à trancher).

---

## PARTIE 1 — LE MÉCANISME

### 1.1 Ce qui existait, et ce qui n'existait pas

Avant ce lot, l'application livreur ouvrait l'accueil dès la session restaurée.
**Aucune condition n'était présentée, aucune acceptation consignée.** C'était la seule
des trois surfaces dans ce cas — et c'est celle dont la relation est la plus engageante :
le coursier prend en charge le bien d'autrui, roule, et transmet sa position en
travaillant.

Les applications acheteur et vendeur avaient déjà leur écran, leur texte et leur route.
Le coursier n'avait ni l'un ni l'autre.

### 1.2 La chaîne complète, bout à bout

| Étape | Où | Ce qui se passe |
|---|---|---|
| 1 | `GET /driver/legal/{terms\|privacy}` | Sert le texte **et sa version** depuis le CMS. Route **publique** : on doit pouvoir lire les conditions d'une plateforme de livraison avant d'y postuler. 404 quand rien n'est publié. |
| 2 | `chargerDocumentLegal` (mobile) | Lit la route. En cas d'échec **ou** de 404, retombe sur la copie embarquée dans le binaire, marquée `servi: false`. Ne lève jamais. |
| 3 | `_PorteConsentement` (mobile) | Lance **en parallèle** `GET /driver/me` et le chargement des conditions, puis décide s'il faut demander l'accord. |
| 4 | `DriverConsentScreen` | Deux onglets, les deux à dérouler jusqu'au bout, une case, `canPop: false`. |
| 5 | `POST /driver/me/accept-terms` | Inscrit la version **sur le compte** (`User.AcceptedTermsVersion`) **et** au registre versionné (`RecordLegalAcceptanceCommand`, slug `legal_terms_driver`, contexte `consent_gate_driver`). |
| 6 | `GET /driver/me` | Rend désormais `acceptedTermsVersion`. C'est ce champ que la porte compare au texte servi. |
| 7 | Compte livreur → « Documents » | Deux entrées pour relire à tout moment, **avec le même rendu** que la porte. |

Emplacements CMS : **`legal_terms_driver`** et **`legal_privacy_driver`**, déclarés dans
`EMPLACEMENTS_LEGAUX` (console d'administration) et dans `DriverLegalEndpoints`.
Ils sont **distincts** de ceux de l'acheteur et du vendeur : le coursier n'a ni les
obligations d'un vendeur ni les droits d'un acheteur, et pour la confidentialité, un
texte acheteur ne parlerait pas de la seule donnée qui le concerne vraiment, sa position.

### 1.3 Les quatre garanties que le code tient

**a) On accepte le texte qu'on a sous les yeux, jamais un autre.**
La porte charge les conditions, décide sur cette base, puis **passe le document à
l'écran**. L'écran ne le relit pas. Dans la première version il le relisait — et il
suffisait que ce second appel échoue là où le premier avait réussi pour que la porte
s'ouvre sur la version 2.0 servie et fasse enregistrer la version 1.0 du binaire. Une
rédaction antérieure à celle déjà acceptée, sur un texte qui n'était pas celui publié.

**b) On ne redemande jamais l'accord vers l'arrière.**
La comparaison est **numérique** (`versionPlusRecente`), pas une inégalité de chaînes.
Un retour arrière dans le CMS, ou une coquille publiant 1.9 après 2.0, aurait autrement
fait réaccepter une rédaction antérieure — et le registre aurait gardé la trace d'un
accord en arrière, indistinguable d'un vrai.
Et l'on n'ouvre **jamais** la porte sur la copie embarquée quand une version a déjà été
acceptée : le repli est plus ancien que ce qui est publié, par construction.

**c) Il faut avoir déroulé les deux textes.**
La case ne s'active qu'après avoir atteint le bas des deux onglets. Le cas d'un texte
**plus court que l'écran** est traité explicitement (`maxScrollExtent <= 0`) : sans lui,
une rédaction condensée — ou simplement une tablette — laissait la case grisée pour
toujours derrière un `canPop: false`, et le coursier ne pouvait ni entrer ni accepter.

**d) Une panne ne ferme pas l'application.**
Si `/driver/me` est injoignable, on laisse passer et la porte se rouvre au lancement
suivant — comme le font l'acheteur et le vendeur. L'écran d'erreur infranchissable qui
avait d'abord été écrit masquait le cas « ce compte n'est pas un coursier » que l'accueil
sait nommer, et cachait le bloc « Aide et assistance » : exactement ce dont un livreur
sans réseau a besoin. Le risque résiduel est mince — sans la marketplace, il n'y a ni
renouvellement de jeton ni course à prendre.

### 1.4 Ce que `recorded: false` veut dire

`POST /driver/me/accept-terms` répond `{ version, recorded }`. `recorded: false` signifie
que la version présentée ne correspond à **aucune version publiée** dans le CMS. C'est le
cas **normal tant que rien n'est publié**, et le cas d'une application ancienne qui
présente sa copie embarquée.

Le consentement est alors consigné **sur le compte** mais **pas au registre versionné**.
C'est un signal pour la plateforme, jamais une sanction pour le coursier : il n'est pas
bloqué. La conséquence pratique est simple — **tant que les documents ne sont pas publiés,
il n'existe aucune trace datée opposable.** C'est la première raison de publier.

### 1.5 Fichiers touchés

**Backend**
- `src/Bff/Marketplace.Bff.Driver/Endpoints/DriverLegalEndpoints.cs` — nouveau
- `src/Bff/Marketplace.Bff.Driver/Endpoints/DriverAccountEndpoints.cs` — `POST /me/accept-terms`, `AcceptedTermsVersion` dans `DriverMeView`
- `src/Bff/Marketplace.Bff.Driver/Program.cs` — enregistrement

**Console d'administration**
- `apps/admin/src/types/admin.ts` — `legal_terms_driver` (accepte) et `legal_privacy_driver` dans `EMPLACEMENTS_LEGAUX`

**Mobile**
- `lib/features/legal/driver/legal_content.dart` — nouveau (le texte)
- `lib/features/legal/driver/legal_data.dart` — nouveau (chargement, repli, comparaison de versions)
- `lib/apps/driver/screens/driver_legal_screen.dart` — nouveau (consultation + `DriverLegalBody`)
- `lib/apps/driver/screens/driver_consent_screen.dart` — nouveau (la porte)
- `lib/apps/driver/driver_app.dart` — `_PorteConsentement` entre la session et l'accueil
- `lib/apps/driver/screens/driver_account_screen.dart` — section « Documents »
- `lib/core/api/driver/driver_api.dart` — `acceptTerms`

**Documents à publier**
- `docs/legal/terms_driver.json`, `docs/legal/privacy_driver.json` — extraits **mécaniquement** de `legal_content.dart` par script, pas recopiés à la main.

---

## PARTIE 2 — LE TEXTE

### 2.1 Ce sur quoi il est exact

Le texte a été écrit **contre le code**, pas contre un modèle. En particulier la
section 3 de la confidentialité, « Votre position — ce qui est réellement conservé » :

> Seule votre DERNIÈRE position connue est conservée, et elle est remplacée à chaque
> nouvelle transmission. Aucun historique n'est constitué.

C'est une **vérification**, pas une formule. Le service de dispatch ne tient que
`LastPosition` et `LastPositionAtUtc`, écrasés à chaque envoi. Il n'existe nulle part de
table d'historique, et le trajet d'un coursier ne peut pas être reconstitué. C'est une
garantie forte, et c'est la seule raison pour laquelle elle est écrite.

Cadre cité : loi n° 2017-20 du 20 avril 2018 portant Code du numérique en République du
Bénin, modifiée par la loi n° 2020-35 du 6 janvier 2021 ; Livre V pour les données
personnelles, sous le contrôle de l'**APDP**.

Statut retenu : **prestataire indépendant**, sans lien de subordination — horaires, zone
et acceptation de chaque course libres. C'est ce que vous avez tranché, et le texte le
dit en toutes lettres (section 2 des conditions).

### 2.2 ⟦DÉCISION⟧ Les six points à trancher avant publication

Ils portent la mention `À COMPLÉTER` **dans le texte lui-même**. Aucun n'est une question
technique : ce sont des engagements de l'entreprise, et je ne les ai pas écrits à votre
place.

| # | Où | Ce qu'il faut décider |
|---|---|---|
| 1 | Conditions §2 — Votre statut | **Assurance.** Quelle couverture est exigée du livreur (responsabilité civile professionnelle, assurance du véhicule) ? Laquelle est le cas échéant fournie par la plateforme ? Qu'exige-t-on à l'inscription ? |
| 2 | Conditions §5 — Rémunération | **Rythme et seuil de versement.** Périodicité, montant minimum de retrait, frais éventuellement retenus. |
| 3 | Conditions §6 — Incidents et dommages | **Responsabilité sur les colis.** Qui supporte la perte ou la détérioration pendant le transport, dans quelle limite, selon quelle procédure. **C'est la clause la plus lourde de conséquences**, pour le livreur comme pour la plateforme. |
| 4 | Conditions §7 — Fin de la relation | **Préavis.** Une résiliation sans manquement suppose-t-elle un délai de prévenance, de part et d'autre ? |
| 5 | Confidentialité §3 — Position | **Effacement au passage hors ligne.** Voir 2.3 ci-dessous : c'est le seul point qui appelle aussi du code. |
| 6 | Confidentialité §6 — Durées | **Conservation après la fin de la relation.** Combien de temps le compte et les pièces justificatives sont-ils gardés une fois que le livreur a cessé de livrer ? |

### 2.3 Le point 5 mérite un paragraphe à lui seul

La dernière position connue **reste enregistrée après le passage hors ligne**, jusqu'à la
transmission suivante. Ce n'est pas un historique, et le trajet reste non reconstituable —
mais un coursier qui se met hors ligne chez lui laisse cette dernière valeur en base
jusqu'à sa prochaine connexion.

Deux suites possibles :

- **Effacer `LastPosition` au passage hors ligne.** C'est quelques lignes dans le service
  de dispatch, et la phrase devient : « Quand vous passez hors ligne, votre dernière
  position est effacée. » C'est nettement plus protecteur, et c'est vérifiable.
- **Ne rien changer**, et garder la phrase exacte telle qu'elle est écrite aujourd'hui.

Le texte actuel dit la vérité plutôt que la version flatteuse. **Il ne faut pas publier la
formulation protectrice avant d'avoir fait le changement** — un texte contractuel qui
décrit un comportement que le code n'a pas est exactement le genre d'écart qui se retourne
contre l'entreprise.

### 2.4 ⟦DÉCISION⟧ Les mentions « À COMPLÉTER » sont AFFICHÉES au livreur

C'est délibéré, et c'est à arbitrer.

Tant que le CMS n'est pas alimenté, c'est la copie embarquée qui s'affiche — **et c'est
elle que la porte fait accepter**. Le livreur lit donc aujourd'hui, dans le contrat qu'on
lui présente, des notes de rédaction qui vous sont adressées. Un contrat accepté dont les
clauses d'assurance, de responsabilité et de rémunération sont des notes internes n'est
pas sérieusement opposable.

Deux garde-fous ont été posés, mais ils ne remplacent pas la décision :

1. L'en-tête affiche, sur la copie embarquée uniquement :
   « Texte provisoire embarqué dans l'application. La version publiée par HBA Express fait
   foi dès qu'elle est disponible. »
2. Les mentions restent visibles plutôt que masquées : **les masquer ferait publier un
   texte incomplet sans que personne ne s'en aperçoive.** Une note gênante est une
   contrainte qui force la décision ; un trou silencieux ne force rien.

**La bonne suite est de trancher les six points, puis de publier** — pas de retirer les
mentions. Si l'application doit être mise entre les mains de livreurs avant cela, il faut
le dire, et l'on choisira alors entre publier un texte volontairement incomplet ou
différer la porte.

### 2.5 Comment publier

Un **seul bloc** par document, type `Page`, position `0`, dont le contenu est le fichier
JSON entier — même procédure que les quatre documents acheteur et vendeur
(`docs/legal/README.md`).

| Fichier | Emplacement CMS | Route |
|---|---|---|
| `docs/legal/terms_driver.json` | `legal_terms_driver` | `GET /driver/legal/terms` |
| `docs/legal/privacy_driver.json` | `legal_privacy_driver` | `GET /driver/legal/privacy` |

**La version est `1.0`, un NUMÉRO et non une date.** C'est volontaire : les documents
acheteur et vendeur portent `2026-07-12`, hérité d'un temps où la version était une date,
et le serveur **refuse de l'inscrire au registre versionné** parce que
`LegalVersionNumber.Parse` n'y voit pas un numéro. Le livreur part avec la bonne forme dès
le départ ; la trace du repli et celle du document publié désigneront la même rédaction.

Toute modification de **fond** change la version, et tout le parc installé redemande
l'accord à la session suivante — sans passer par les stores. Une correction de pure forme
peut garder la version : c'est une décision, pas une mécanique.

### 2.6 Avant la première publication

Ces fichiers sont une proposition rédigée en lisant le code, **pas une relecture
juridique**. Le fond doit être validé par un juriste béninois — les six points du 2.2 en
priorité, la clause de responsabilité sur les colis d'abord.
