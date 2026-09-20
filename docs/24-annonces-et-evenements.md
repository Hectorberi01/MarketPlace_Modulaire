# 24 — Annonces et événements : audit et modèle d'affichage

Date : 20/09/2026. Portée : module `Marketing`, BFF Client et Admin, vitrine acheteur
(`apps/boutique`), console d'administration (`apps/admin`), application mobile acheteur.

---

## 0. Résumé

Trois constats, par ordre de gravité.

1. **La fenêtre d'affichage n'est appliquée nulle part.** La console laisse saisir une
   date de début et de fin sur chaque bannière, les stocke, et les réaffiche dans la
   liste. Aucune requête ne les compare jamais à une horloge. Une bannière publiée
   s'affiche immédiatement et ne disparaît jamais d'elle-même. C'est une promesse
   silencieuse de l'interface, et c'est le défaut à corriger en premier.
2. **Les campagnes n'existent pour aucun client.** `Campaign` porte déjà les types
   `FlashSale`, `Event`, `Seasonal`, un statut, une fenêtre, et une liste de promotions.
   La console sait les créer, les planifier, les activer. Aucune route du BFF Client ne
   les expose : ni la vitrine ni l'application mobile ne savent qu'un événement existe.
   Une vente flash ne se voit qu'à travers un prix barré sur une vignette.
3. **Il n'y a aucune « annonce » au sens d'un message d'information.** Le seul objet
   diffusable est une bannière IMAGE, visible sur la seule page d'accueil. Rien ne permet
   de dire « les livraisons sont suspendues jeudi » à un acheteur qui est déjà dans son
   panier.

---

## 1. Trois objets que l'on confond, et qu'il faut séparer

| Objet | Ce que c'est | Durée de vie | Porteur |
|---|---|---|---|
| **Annonce** | Un message court et daté : incident, jour férié, changement de règle, nouveauté. Du texte, pas une création graphique. | Heures à quelques jours | N'existe pas |
| **Bannière** | Un visuel promotionnel du carrousel d'accueil. Une image, un lien, un titre. | Semaines | `ContentBlock` type `Banner`, slot `home_banner` |
| **Événement** | Une opération commerciale : vente flash, Black Friday, rentrée. Elle regroupe des promotions et a un début et une fin. | Jours à semaines | `Campaign` (types `FlashSale`, `Event`, `Seasonal`) |

Les confondre coûte cher dans les deux sens : traiter une annonce comme une bannière
oblige à fabriquer une image pour dire trois lignes, et traiter un événement comme une
bannière lui retire sa page, sa liste de produits et son compte à rebours.

---

## 2. Inventaire de l'existant

| | Modèle | Console admin | API | Web client | Mobile client |
|---|---|---|---|---|---|
| Bannière | `ContentBlock` | `/banners` | `GET /client/home/banners` | carrousel d'accueil | carrousel d'accueil |
| Bloc éditorial | `ContentBlock` (`Page`, `FeaturedList`, `Faq`) | `/banners` (bannières seules) | `GET /client/content/{slot}` | FAQ `/aide` | FAQ |
| Document juridique | `LegalDocument` + versions | `/legal` | `GET /client/content/legal/{doc}` | 4 pages `/legal/*` | écrans légaux |
| Campagne | `Campaign` | `/marketing` | `/admin/campaigns/*` uniquement | **rien** | **rien** |
| Promotion | module `Pricing` / `Offers` | `/marketing` | prix barré sur la vignette | prix barré | prix barré |
| Notification | module `Notifications` | `/notifications` | envoi à **un** destinataire | — | centre de notifications |

Points d'appui : l'infrastructure éditoriale est déjà là (agrégat, publication, position,
repli de configuration, console) et le carrousel est alimenté par la même route pour le
web et le mobile — une source unique, ce qui est le bon point de départ.

---

## 3. Défauts constatés

### D1 — La fenêtre d'affichage est stockée, affichée, et jamais appliquée

`MarketingRepositories.cs:40` :

```csharp
.Where(b => b.Slot == slot && b.Status == ContentStatus.Published)
```

`StartUtc` et `EndUtc` ne sont comparés à une horloge dans AUCUN fichier du module : ni
dépôt, ni handler, ni BFF. Vérifié par recherche exhaustive — les seules occurrences sont
des affectations, des projections et des transports.

Conséquences, dans l'ordre où elles se produisent :

- une bannière préparée à l'avance et publiée part en production immédiatement ;
- une bannière de Noël reste en ligne en janvier, jusqu'à ce que quelqu'un la dépublie ;
- l'opérateur ne peut pas le deviner : la console lui montre la fenêtre qu'il a saisie
  (`banners/page.tsx:118`), ce qui lui confirme qu'elle est prise en compte.

### D2 — La saisie au jour : un décalage d'une heure, et une journée impossible

CORRECTION DU 20/09/2026. La première rédaction de cette fiche affirmait que la console
envoyait la date de fin à `T00:00:00Z`, et donc qu'une bannière disparaîtrait un jour trop
tôt. C'est FAUX : j'avais lu la ligne `startUtc` et supposé la suivante. Le code envoie

```ts
startUtc: form.start ? `${form.start}T00:00:00Z` : null,
endUtc:   form.end   ? `${form.end}T23:59:59Z`   : null,
```

c'est-à-dire le jour entier, ce qui est correct. Restent deux points mineurs :

1. Les bornes sont en UTC alors que le Bénin est à UTC+1 : une bannière « jusqu'au 30 »
   s'éteint le 1er à 00:59:59 locales. Une heure de trop, sans conséquence commerciale.
   Corriger demanderait une conversion aller-retour asymétrique dans le formulaire et
   décalerait la lecture des bannières déjà enregistrées : le rapport n'y est pas.
   Le formulaire dit désormais explicitement que les bornes sont en UTC.
2. La validation du formulaire refusait `fin <= début`, donc refusait une bannière d'UNE
   journée (début et fin le même jour), alors que les instants correspondants — 00:00:00Z
   et 23:59:59Z — sont parfaitement ordonnés. Corrigé en `fin < début`.

### D3 — Aucune annonce textuelle, et aucune surface pour en afficher une

Le seul emplacement diffusé est `home_banner`, rendu par le carrousel de la page
d'accueil. Un acheteur qui arrive par une fiche produit, qui est dans son panier ou dans
le tunnel de commande ne voit jamais rien. Or c'est exactement là qu'une information
opérationnelle a de la valeur (« paiement Mobile Money indisponible ce soir »).

### D4 — Les campagnes ne sortent pas de la console

Aucune route `client` ne mentionne `campaign`. Il n'existe donc ni page d'événement, ni
liste des opérations en cours, ni moyen pour un acheteur de savoir qu'une vente flash se
termine dans deux heures. Le travail de modélisation est fait ; la moitié client manque
entièrement.

### D5 — Une campagne planifiée ne s'active jamais toute seule

`Campaign` a bien `Schedule()`, `Activate()`, `End()`, mais aucun service d'arrière-plan
ne les appelle : la transition dépend d'un clic dans la console. Une vente flash qui
commence à 8 h commence donc quand quelqu'un se lève. Le précédent existe pourtant dans
le dépôt — `DiscountExpiryService` (module `Offers`) fait exactement ce genre de balayage.

### D6 — Les notifications sont strictement un-à-un

`SendNotificationCommand(request.RecipientUserId, …)` : pas de segment, pas de diffusion.
Annoncer quelque chose à tous les acheteurs demanderait aujourd'hui une boucle sur la
table des utilisateurs depuis la console, ce qui n'existe pas et ne devrait pas exister
sous cette forme.

### D7 — Une bannière sans titre est un lien sans nom

Dans le carrousel, l'image porte `alt=""` (correct : le titre la décrit juste en dessous),
mais le titre est conditionnel. Quand il est absent et que la bannière est cliquable, le
lien ne contient plus aucun texte : un lecteur d'écran annonce un lien anonyme. Le titre
devrait être obligatoire dès qu'un lien est renseigné.

### D8 — Les emplacements sont des chaînes magiques dupliquées

`"home_banner"` est écrit dans le BFF et dans la console ; `help_faq` dans le BFF et dans
la vitrine. Il n'existe aucune table de référence des emplacements, alors que le travail
juridique en a produit une (`EMPLACEMENTS_LEGAUX`) précisément parce qu'une faute de
frappe y était indétectable : le bloc est publié, la page reste vide, et rien ne signale
l'écart.

---

## 4. Modèle proposé

### 4.1 L'annonce : un type de plus sur `ContentBlock`, pas un nouvel agrégat

Recommandation : ajouter `ContentBlockType.Announcement` plutôt que créer un agrégat.

Pourquoi ne pas refaire ce qu'on a fait pour le juridique : `LegalDocument` a mérité son
propre agrégat parce qu'il lui fallait un historique de versions, une preuve de
consentement et une immuabilité après publication. Une annonce n'a besoin d'aucune des
trois. Elle a besoin d'un statut, d'une fenêtre et d'une position — exactement ce que
`ContentBlock` porte déjà, avec sa console, sa publication et son archivage.

Contenu (le champ `Content`, JSON, comme les bannières) :

```json
{
  "niveau": "info | succes | attention | critique",
  "titre": "Livraisons suspendues jeudi 25",
  "message": "Les tournées reprennent vendredi matin. Les commandes déjà payées partent en priorité.",
  "lien": "/aide/livraison",
  "libelleLien": "En savoir plus",
  "fermable": true
}
```

Emplacements (slots) à prévoir, un par portée d'affichage :

| Slot | Où il s'affiche |
|---|---|
| `announcement_global` | Toutes les pages de la vitrine et l'accueil mobile |
| `announcement_checkout` | Panier et tunnel de commande uniquement |
| `announcement_seller` | Console vendeur et application vendeur |

Le découpage par slot évite un champ « portée » à interpréter côté client : chaque surface
lit l'emplacement qui la concerne, et ne connaît pas les autres.

### 4.2 L'événement : compléter `Campaign`, puis l'exposer

Trois champs manquent pour qu'une campagne soit affichable :

- `Slug` — l'URL publique (`/evenements/rentree-2026`) ;
- `HeroImageUrl` et `Description` — de quoi dessiner une page sans inventer ;
- `IsPublic` — une campagne peut n'être qu'un regroupement interne de promotions ; toutes
  ne méritent pas une page.

Et deux routes côté BFF Client :

```
GET /client/events              → campagnes publiques ACTIVES, triées par fin la plus proche
GET /client/events/{slug}       → l'événement + les produits de ses promotions
```

La seconde est la seule qui demande un vrai travail : elle doit résoudre
`PromotionIds` → produits concernés, ce que le module `Pricing` sait faire pour un produit
donné mais pas encore dans l'autre sens.

### 4.3 La fenêtre, corrigée une fois pour toutes

Un seul endroit doit décider si un bloc est visible :

```csharp
.Where(b => b.Slot == slot
         && b.Status == ContentStatus.Published
         && (b.StartUtc == null || b.StartUtc <= maintenantUtc)
         && (b.EndUtc   == null || b.EndUtc   >  maintenantUtc))
```

avec `maintenantUtc` passé par l'horloge injectée, jamais `DateTime.UtcNow` en dur — c'est
ce qui rend la règle testable sans attendre le mois prochain.

Côté console, la date de fin est DÉJÀ envoyée à la fin du jour choisi (`T23:59:59Z`) :
elle s'accorde avec une borne de fin stricte et il n'y a rien à y changer (voir D2, qui
corrige ce que cette fiche affirmait d'abord).

Le cache de la vitrine est de 60 s (`CACHE_PUBLIC_SECONDES`) : une annonce mise en ligne
apparaît dans la minute, et une fenêtre qui se ferme se referme dans la minute. C'est le
bon ordre de grandeur, rien à changer.

---

## 5. Comment les afficher côté client

### 5.1 Annonce — un bandeau, sous l'en-tête, sur toutes les pages

- **Position** : juste sous l'en-tête, AU-DESSUS du contenu, pleine largeur. Pas en
  surimpression, pas en fenêtre modale : une annonce ne doit pas bloquer l'achat.
- **Niveaux** : `info` (fond neutre), `succes` (vert de marque), `attention` (ambre),
  `critique` (rouge). Le niveau porte aussi une icône — la couleur seule ne suffit pas,
  et une partie des acheteurs consulte le site en plein soleil sur un écran de téléphone.
- **Fermeture** : bouton « fermer » si `fermable`, mémorisé par identifiant de bloc dans
  le `localStorage`. Mémoriser par identifiant, et non par slot, sinon la fermeture d'une
  annonce masque la suivante.
- **Tunnel de commande** : seul `announcement_checkout` s'y affiche. Une promotion
  n'a rien à faire au-dessus d'un formulaire de paiement ; une indisponibilité du paiement,
  si.
- **Mobile** : même contenu, même route, un bandeau en haut de l'accueil. Une annonce de
  niveau `critique` mérite en plus une carte dans le centre de notifications, pour rester
  consultable après fermeture.

Composant : `apps/boutique/src/components/bandeau-annonce.tsx`, monté dans
`app/layout.tsx` sous `<Entete />`. Lecture serveur, comme les bannières, donc pas de
scintillement au chargement.

### 5.2 Événement — une page, une liste, et des rappels là où l'on achète

1. **`/evenements`** — la liste des opérations en cours et à venir. Une carte par
   événement : visuel, nom, ce qui se termine quand.
2. **`/evenements/[slug]`** — la page de l'opération : visuel en tête, description,
   **compte à rebours** jusqu'à la fin, puis la grille des produits concernés, réutilisant
   `GrilleProduits` tel quel.
3. **Sur les vignettes produit** — une pastille « Vente flash » quand le produit fait
   partie d'une campagne active, à côté du prix barré qui existe déjà. C'est ce qui
   transforme une remise anonyme en événement.
4. **Dans la navigation** — une entrée n'apparaît dans l'en-tête que s'il existe au moins
   une campagne publique active. Un menu « Événements » qui mène à une page vide use la
   confiance plus vite qu'il ne rapporte de clics.
5. **Sur l'accueil** — une bande « Se termine bientôt » sous le carrousel, pour les
   campagnes dont la fin est à moins de 48 h.

Le compte à rebours se calcule à partir de `EndUtc` rendu par l'API, jamais à partir de
l'horloge du téléphone seule : sur un appareil mal réglé, un compte à rebours faux est
pire que pas de compte à rebours.

### 5.3 Où ne rien afficher

- Pas d'annonce ni de bannière dans les pages juridiques : ce sont des documents
  contractuels, on ne les entoure pas de promotions.
- Pas de bandeau promotionnel dans le tunnel de paiement (voir 5.1).
- Pas de fenêtre modale à l'ouverture de l'application : c'est le moyen le plus rapide de
  faire désinstaller une application de commerce.

---

## 6. Plan de travail

| Lot | Contenu | Pourquoi dans cet ordre |
|---|---|---|
| **0** | FAIT le 20/09/2026 — voir ci-dessous | |
| **1** | FAIT le 20/09/2026 — voir ci-dessous | |
| **2** | FAIT le 20/09/2026 — voir §9 | |
| **3** | FAIT le 20/09/2026 — voir §9 | |
| **4** | `Slug`, `HeroImageUrl`, `Description`, `IsPublic` sur `Campaign` + migration | Prépare l'exposition sans rien casser. |
| **5** | Routes `/client/events` et `/client/events/{slug}` | La résolution promotions → produits est le vrai travail. |
| **6** | Pages `/evenements`, pastilles sur les vignettes, entrée de menu conditionnelle | L'affichage, une fois les données disponibles. |
| **7** | Balayage d'arrière-plan des campagnes (D5), sur le modèle de `DiscountExpiryService` | Confort d'exploitation, pas bloquant. |
| **8** | Diffusion segmentée des notifications (D6) | Le plus gros morceau, et le seul qui touche à la vie privée : une annonce poussée à tout le monde doit être justifiable. |

Les lots 0 et 1 sont de l'ordre de la demi-journée. Le lot 5 est le seul qui demande une
décision d'architecture (voir ci-dessous).

---

## 7. Décisions qui restent à prendre

1. **Résolution promotions → produits.** Interroger `Pricing` produit par produit ne tient
   pas à l'échelle d'une page d'événement. Soit `Pricing` expose « les produits d'une
   promotion », soit `Marketing` conserve une liste de produits mise à jour par événement
   d'intégration. La première est plus juste, la seconde plus rapide à afficher.
2. **Fuseau horaire de saisie.** Les dates saisies en console sont-elles à lire en UTC ou
   à l'heure de Cotonou (UTC+1) ? Tant que ce n'est pas tranché, « fin le 30 » reste
   ambigu d'une heure à un jour près.
3. **Une annonce doit-elle être bilingue ?** Le mobile a déjà `app_fr.arb` / `app_en.arb`.
   Une annonce rédigée dans la console n'a, elle, qu'une seule langue.
4. **Le vendeur reçoit-il les mêmes annonces que l'acheteur ?** Le slot
   `announcement_seller` le suppose distinct ; à confirmer avant de construire l'écran
   d'administration, car cela change le nombre d'emplacements à gérer.


---

## 8. Ce qui a été fait — lots 0 et 1, 20/09/2026

### Lot 0 — la fenêtre d'affichage s'applique

| Fichier | Changement |
|---|---|
| `…Domain/Content/IContentBlockRepository.cs` | `ListPublishedBySlotAsync` prend un `DateTime asOfUtc` OBLIGATOIRE. Sans valeur par défaut, délibérément : un `= default` aurait valu `DateTime.MinValue` chez l'appelant distrait, et toutes les fenêtres à venir auraient été considérées comme fermées — un slot vide, sans la moindre erreur. |
| `…Infrastructure/Persistence/MarketingRepositories.cs` | Le filtre `(StartUtc == null ou <= asOf) et (EndUtc == null ou > asOf)`, traduit en SQL. Deux bornes nulles redonnent exactement l'ancien comportement : les blocs déjà en ligne ne bougent pas. |
| `…Application/Content/ContentCommandsAndQueries.cs` | Appelant. La requête `ListContentBySlotQuery` est documentée comme la vue du VISITEUR. |
| `…Application/Content/LegalDocumentQuery.cs` | Appelant. La fenêtre s'applique aussi aux anciens blocs juridiques : un texte sorti de sa période ne doit plus être servi comme contrat en vigueur. |
| `Bff.Admin/Endpoints/AdminPlatformEndpoints.cs` | `GET /admin/content/slot/{slot}` ne passe PLUS par la requête visiteur. Sinon une bannière programmée ou expirée disparaissait de la console — c'est-à-dire de l'écran où l'on corrige justement ses dates. |
| `apps/admin/.../banners/page.tsx` | Pastille « Programmée / En ligne / Expirée » à côté du statut : « publiée » ne veut plus dire « visible ». La confirmation de publication ne promet plus « dès maintenant » quand la période commence plus tard. Bannière d'UNE journée autorisée (`fin < début` au lieu de `fin <= début`). Borne de fin portée à `23:59:59.999Z` pour refermer le trou de la dernière seconde ouvert par la borne exclue. Le fuseau (UTC, Bénin à UTC+1) est écrit sous le formulaire. |

CONVENTION À TENIR : début inclus à `00:00:00.000Z`, fin exclue à `23:59:59.999Z`. Les deux
moitiés se tiennent — changer l'une sans l'autre rouvre un trou en fin de journée, qu'aucune
lecture de code ne révèle.

### Lot 1 — le titre, et les emplacements

| Fichier | Changement |
|---|---|
| `apps/admin/.../banners/page.tsx` | Une bannière CLIQUABLE doit avoir un titre : c'est lui qui nomme le lien. Le libellé du champ disait « Titre (interne) », ce qui était faux — il est affiché sous l'image dans les deux carrousels. |
| `apps/boutique/src/app/page.tsx` | Repli `aria-label` pour les bannières déjà enregistrées sans titre. Absent quand le titre existe, sinon il masquerait le texte visible. |
| `hba-express-mobile/.../home/customer/presentation/home_screen.dart` | La bannière mobile n'annonçait RIEN : une image dans un `GestureDetector` n'a ni rôle ni nom, et le carrousel — première chose de l'accueil — n'existait pas pour TalkBack ni VoiceOver. `Semantics(button:, image:, label:)` posé. |
| `…Marketing.Contracts/ContentSlots.cs` (nouveau) | Les emplacements, écrits une fois côté serveur. |
| `apps/admin/src/types/admin.ts` | `EMPLACEMENTS_CONTENU` + les slugs nommés, côté console. |
| `scripts/verifier-emplacements.mjs` + `npm run verify:emplacements` | Confronte les deux listes. Vérifié en cassant les trois règles une par une : faute de frappe côté console, emplacement serveur non documenté, constante nommée hors table — les trois sont attrapées. Quand `marketPlace` n'est pas à côté (compilation du dépôt web seul), le contrôle le dit et rend la main sans échouer. |

Contrôles passés : `tsc`, `next lint` et `next build` sur la boutique et la console ;
`verify:espaces` et `verify:emplacements` ; équilibre des délimiteurs sur le fichier Dart.
Le C# n'a pas pu être compilé (aucun SDK .NET disponible) : il a été relu ligne à ligne,
appelants et implémenteurs recherchés dans tout le dépôt, types de colonnes confirmés dans
la migration et le snapshot.

### À faire à la mise en production

`dotnet build Marketplace.sln`. **Aucune migration** : le schéma ne change pas, seule une
requête change. Vérifier après déploiement qu'une bannière publiée avec une date de début
future n'apparaît PAS sur la vitrine, et qu'elle est bien marquée « Programmée » dans la
console.


---

## 9. Ce qui a été fait — lots 2 et 3, 20/09/2026

### Lot 2 — l'annonce existe et se rédige

| Fichier | Changement |
|---|---|
| `…Domain/Content/ContentBlock.cs` | `ContentBlockType.Announcement = 4`, avec le raisonnement : pourquoi un type de bloc et non un agrégat séparé. |
| `…Contracts/ContentSlots.cs` | `announcement_global` et `announcement_checkout`. La portée EST l'emplacement : un champ « portée » aurait obligé chaque client à filtrer correctement une valeur qui ne le concerne pas. Pas d'emplacement vendeur tant que rien ne le lit. |
| `…Application/Content/ContentCommandsAndQueries.cs` | Message du validateur complété. |
| `Bff.Client/Endpoints/ClientContentEndpoints.cs` | `GET /client/announcements/{portee}`. Table FERMÉE des portées, comme pour les documents légaux : sans elle, la portée serait un slot arbitraire et n'importe quel bloc du CMS deviendrait lisible sous forme d'annonce. Une portée inconnue rend une liste vide, pas un 404 — pour le bandeau qui appelle, « rien à afficher » est une réponse valable. Un bloc au JSON illisible est ignoré, jamais rendu dégradé. Le lien est re-contrôlé côté serveur (route interne ou http(s)) bien que la console le contrôle déjà : une valeur servie à trois applications ne doit dépendre d'aucune vigilance en amont. |
| `apps/admin/.../annonces/page.tsx` (nouveau) | Niveau, titre, message, lien, libellé, fenêtre, position, fermable. Écran SÉPARÉ de celui des bannières : un écran unique aurait demandé de choisir un type, puis un emplacement, puis une forme de charge JSON — c'est-à-dire de connaître le modèle pour écrire trois lignes. |
| `apps/admin/src/types/admin.ts` | `PORTEES_ANNONCE`, `NIVEAUX_ANNONCE`, `ContenuAnnonce`, et les deux emplacements dans `EMPLACEMENTS_CONTENU` — le garde-fou `verify:emplacements` les confronte au serveur. |
| `apps/admin/src/components/sidebar.tsx` | Entrée « Annonces », icône `BellRing` — `Megaphone` sert déjà à « Marketing », et deux entrées au même pictogramme se confondent dans la barre repliée. |

RÈGLE POSÉE DANS L'ÉCRAN : une annonce NON FERMABLE doit avoir une date de fin. Le
visiteur ne peut pas la retirer, et sans fin plus rien ne la retire non plus — seul un
passage dans la console y mettrait un terme.

### Lot 3 — le bandeau, sur les deux surfaces

| Fichier | Changement |
|---|---|
| `apps/boutique/src/lib/annonces.ts` (nouveau) | Lecture serveur, cache 60 s, contrôle de forme AVANT l'affichage. Aucun repli : « aucune annonce » est la situation normale. |
| `apps/boutique/src/components/annonces-affichage.tsx` (nouveau) | Le rendu et la fermeture. Quatre niveaux, chacun avec icône ET préfixe lu par les lecteurs d'écran — la couleur seule ne suffit pas sur un téléphone en plein soleil. `role="alert"` pour le seul niveau critique. Fermeture mémorisée PAR IDENTIFIANT dans `localStorage` : par emplacement, elle aurait masqué toutes les annonces suivantes. Lecture du stockage après le montage, pas pendant le rendu — sinon l'arbre du serveur et celui du navigateur divergent. |
| `apps/boutique/src/components/bandeau-annonce.tsx` (nouveau) | L'enveloppe serveur. Deux placements : « bandeau » hors du conteneur (mise en page), « encart » à l'intérieur d'une page. |
| `apps/boutique/src/app/layout.tsx` | Portée globale, sous l'en-tête, au-dessus du contenu. |
| `apps/boutique/src/app/{panier,commande}/page.tsx` | Portée commande, en encart. |
| `hba-express-mobile/.../home/customer/home_data.dart` | Modèle `Annonce`, `annoncesProvider`, `annoncesFermeesProvider`. Fermeture EN MÉMOIRE : le dépôt n'embarque pas `shared_preferences`, et détourner le coffre à secrets pour une liste d'identifiants serait déplacé. Une annonce fermée réapparaît donc au prochain lancement. |
| `hba-express-mobile/.../presentation/announcement_banner.dart` (nouveau) | Le bandeau mobile. Seules les adresses http(s) sont suivies : les routes internes du site (« /aide/livraison ») n'existent pas dans l'application (« /account/faq »), et pousser l'une pour l'autre mènerait à une route inexistante au moment précis où l'on cherchait à rassurer. La console prévient le rédacteur quand il saisit un chemin interne. |
| `hba-express-mobile/.../presentation/home_screen.dart` | Le bandeau est monté HORS de `feed.when`. Posé dans la branche `data`, il n'existait que si l'accueil avait chargé — or le moment où une annonce compte le plus est celui où le reste de l'API échoue aussi. |

### Vérifications

- `tsc` sur les trois applications web, `next lint` et `next build` sur la boutique et la console, `verify:espaces` et `verify:emplacements`.
- Rendu du bandeau vérifié EMPIRIQUEMENT : bouchon temporaire de deux annonces (une critique non fermable avec lien, une informative fermable), compilation, puis lecture du HTML produit — niveaux, icônes, préfixe `sr-only`, lien, bouton de fermeture présent seulement sur la fermable, et les deux placements (une seule enveloppe pleine largeur, l'encart sans conteneur). Bouchon retiré et recompilation propre.
- C# et Dart non compilables ici : relus ligne à ligne par un second passage adversarial. Deux défauts réels trouvés et corrigés — le bandeau mobile invisible hors de la branche `data`, et quatre constructeurs `const` manquants que `flutter analyze` aurait refusés (`prefer_const_constructors` est activé et les infos y sont fatales).

### À faire à la mise en production

`dotnet build Marketplace.sln`. **Aucune migration** : `Announcement` est une valeur d'énumération stockée en entier, aucune colonne ne change. Publier une annonce de test en portée globale, vérifier qu'elle apparaît sous l'en-tête du site dans la minute (cache de 60 s) et en tête de l'accueil de l'application, puis l'archiver.
