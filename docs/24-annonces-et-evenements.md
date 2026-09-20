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
| **4** | FAIT le 20/09/2026 — voir §10 | |
| **5** | FAIT le 20/09/2026 — voir §10 | |
| **6** | FAIT le 20/09/2026 — voir §11 | |
| **7** | FAIT le 20/09/2026 — voir §11 | |
| **8** | FAIT le 20/09/2026 — voir §11 | |

Les lots 0 et 1 sont de l'ordre de la demi-journée. Le lot 5 est le seul qui demande une
décision d'architecture (voir ci-dessous).

---

## 7. Décisions qui restent à prendre

1. **Résolution promotions → produits. TRANCHÉ le 20/09/2026 : ni l'une ni l'autre.**
   En lisant `Promotion`, il s'est avéré qu'elle porte DÉJÀ un périmètre (`ScopeType`) et
   des cibles (`Targets`), et que `PromotionSummary` les expose. Il n'y avait donc rien à
   ajouter à `Pricing`, et surtout rien à dénormaliser dans `Marketing` : la seconde
   option aurait créé une liste de produits à tenir à jour en plus de celle qui existe,
   avec sa propre dérive. Le BFF résout chaque périmètre avec les lectures existantes —
   voir §10.
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


---

## 10. Ce qui a été fait — lots 4 et 5, 20/09/2026

### Lot 4 — une campagne peut être montrée

| Fichier | Changement |
|---|---|
| `…Domain/Campaigns/Campaign.cs` | `Slug`, `Description`, `HeroImageUrl`, `IsPublic`, plus `DefinirPresentation` et `EstVisiblePubliquement(asOfUtc)`. Le slug est normalisé et validé DANS le domaine : c'est une adresse, donc une donnée du domaine, et aucune translittération silencieuse — « rentrée » est refusé plutôt que transformé en une adresse que personne n'a écrite. Le visuel doit être une URL http(s), contrôlée pour de bon : la propriété le promettait en commentaire, rien ne l'imposait, et la valeur est servie anonymement à trois applications. |
| `…Infrastructure/Persistence/Configurations/MarketingConfigurations.cs` | Quatre colonnes et un index unique FILTRÉ sur `Slug IS NOT NULL` : sans filtre, l'unicité n'aurait fonctionné que par accident (PostgreSQL tient les NULL pour distincts), et le filtre documente que l'absence d'adresse est le cas ordinaire. |
| `Migrations/20260920090000_AddCampaignPublicPresentation.{cs,Designer.cs}` + snapshot | Écrits à la main, aucun SDK .NET disponible. `IsPublic` est NOT NULL avec défaut `false` : les campagnes déjà en base gardent exactement le comportement qu'elles avaient, aucune n'est publiée par accident. Designer et snapshot vérifiés identiques octet pour octet. |
| `…Application/Campaigns/CampaignCommandsAndQueries.cs` | `SetCampaignPresentationCommand` SÉPARÉE de `UpdateCampaignCommand`. Les fusionner aurait fait qu'un appelant ne connaissant pas les quatre champs les efface en enregistrant le nom — c'est le défaut que `targetSegment` a déjà coûté une fois dans cet écran. Plus un validateur de longueurs : EF ne valide pas `MaxLength` côté client, et une saisie trop longue serait revenue en 500. |
| `Bff.Admin/Endpoints/AdminPlatformEndpoints.cs` | `PUT /admin/campaigns/{id}/presentation`. |
| `Bootstrap/…/UniqueViolationExceptionHandler.cs` | La correspondance se fait par FRAGMENT : « ix_campaigns_slug » contient « slug », et l'administrateur d'une campagne s'entendait répondre « un produit portant ce nom existe déjà ». Les index nommés passent maintenant avant le cas générique — campagnes et documents juridiques compris. |
| `apps/admin/.../marketing/page.tsx` | Dialogue « Page publique » : adresse, présentation, visuel, et la case qui décide. La même règle de slug est dite AVANT l'envoi, parce qu'une adresse refusée après un aller-retour se corrige à l'aveugle. Pastille « Publique » dans la liste, qui dit que la campagne a une page — pas qu'elle est visible, ce qui dépend aussi du statut et de la fenêtre. |

### Lot 5 — les routes client, et la résolution

`GET /client/events` et `GET /client/events/{slug}`, anonymes.

LA RÈGLE DE VISIBILITÉ VIT DANS L'AGRÉGAT. `EstVisiblePubliquement` exige les quatre
conditions — publique, active, dans la fenêtre, pourvue d'une adresse. Le BFF ne la
réécrit pas : deux endroits qui décident séparément finissent par ne plus être d'accord,
et c'est la vitrine qui tranche, au pire moment. Le dépôt ne fait qu'un pré-filtrage sur
des conditions NÉCESSAIRES (`IsPublic` et `Slug != null`) pour ne pas charger
l'historique marketing entier sur une route anonyme ; il ne peut donc jamais masquer ce
que la règle aurait montré.

« N'existe pas » et « pas encore publique » rendent la MÊME réponse : distinguer les deux
dirait à un visiteur qu'une opération se prépare sous ce nom.

**La résolution promotions → produits**, décision n°1 tranchée :

| Périmètre de la promotion | Ce que la route en fait |
|---|---|
| `Product` | Les cibles SONT des produits : lecture publique, une par une. Un produit archivé ou rejeté depuis est écarté — l'afficher le rendrait cliquable vers une fiche qui n'existe plus. |
| `Category` | Une seule recherche, filtrée sur l'ensemble des catégories visées. |
| `Seller` | Une recherche par vendeur visé : l'index n'en filtre qu'un à la fois. |
| `Cart`, `Global` | Aucun produit : l'opération porte sur toute la boutique. La page devra le dire plutôt que d'afficher une grille vide. |

Les vignettes sortent dans la MÊME forme que la recherche, pour que la grille d'un
événement réutilise la carte produit existante. Un objet « produit d'événement » aurait
imposé une seconde carte, qui aurait divergé de la première au premier changement de
design.

Deux bornes assumées : 48 produits par page — une page d'événement donne envie, elle ne
déroule pas un catalogue — et des lectures SÉQUENTIELLES, parce que les DbContext des
modules ne sont pas utilisables en parallèle. C'est la contrainte qui gouverne déjà
`ClientBff.WithProductImagesAsync`.

### Vérifications

`tsc`, `next lint` et `next build` sur la console ; les deux garde-fous ; Designer et
snapshot de migration comparés programmatiquement. Le C# a été relu par un passage
adversarial qui a confirmé la migration par différentiel (le `Verification` vide de la
veille prouve que le snapshot était aligné avant) et a trouvé six défauts, tous corrigés :
l'ordre des deux contrôles de slug, le message d'unicité qui parlait d'un produit,
l'absence de validateur de longueurs, le contrat http(s) du visuel qui n'était qu'un
commentaire, la lecture de toute la table des campagnes sur une route anonyme, et une
double lecture des offres par produit.

### À faire à la mise en production

`dotnet build Marketplace.sln`, puis **appliquer la migration sur une base de recette
d'abord** — elle s'applique au démarrage de l'API, et une migration qui échoue relance la
pile en boucle. Ensuite `dotnet ef migrations add Verification2` doit produire une
migration VIDE ; si elle ne l'est pas, le snapshot écrit à la main diverge du modèle et
c'est lui qu'il faut corriger, pas la base.

Reste le lot 6 : les pages `/evenements` de la vitrine, les pastilles « Vente flash » sur
les vignettes, et l'entrée de menu conditionnelle.


---

## 11. Ce qui a été fait — lots 6 à 8, 20/09/2026

### Lot 6 — les pages d'événement

`/evenements` et `/evenements/[slug]`, plus l'entrée de menu qui n'apparaît que s'il y a
quelque chose en cours — un menu permanent vers une page qui dit « aucune opération » use
la confiance plus vite qu'il ne rapporte de clics. La PAGE, elle, reste indexable même
vide : une page qui disparaît de l'index entre deux ventes flash perd à chaque fois
l'ancienneté qu'elle a mis des semaines à gagner. Les événements en cours entrent dans le
plan de site.

DEUX ÉCARTS ASSUMÉS PAR RAPPORT À L'AUDIT :

1. **Pas de pastille « Vente flash » sur les vignettes.** Elle aurait demandé de savoir,
   pour chaque produit de chaque grille, s'il appartient à une campagne active — une
   résolution par vignette sur des pages qui en portent vingt-quatre. À la place, la
   carte affiche la FIN DE REMISE, qui arrivait déjà jusqu'à elle depuis le début et
   n'était affichée nulle part : « −30 % » ne dit pas s'il faut se dépêcher, « −30 %
   jusqu'au 25 sept. » le dit. Coût nul, même effet.
2. **Une date sur la liste, un compte à rebours sur la page de l'événement.** Un décompte
   par carte, c'est une minuterie par carte qui réveille React chaque seconde. La règle
   était déjà écrite dans `carte-produit.tsx` ; elle vaut ici aussi.

Le compte à rebours part de la fin rendue par le SERVEUR, jamais de l'horloge seule : sur
un téléphone mal réglé, un compte à rebours faux fait rater l'affaire à quelqu'un qui
croyait avoir le temps.

### Lot 7 — les campagnes commencent et finissent toutes seules

`SweepCampaignsCommand` + `CampaignScheduleService`, calqué sur `DiscountExpiryService`
(même structure, même garde `BackgroundJobs.EnabledOnThisHost` — les cinq hôtes composent
le module).

UNE MINUTE, PAS CINQ. Le balayage des remises tourne toutes les cinq minutes et c'est
très bien pour une remise dont personne ne connaît l'heure exacte. Une vente flash est
annoncée « à 8 h », avec un compte à rebours sous les yeux de l'acheteur : cinq minutes
de retard sur ce rendez-vous se voient.

LA FIN L'EMPORTE SUR LE DÉBUT. Une campagne planifiée dont la fenêtre est entièrement
passée — on la crée, on l'oublie, on la retrouve trois semaines plus tard — n'est pas
activée d'abord pour être terminée au cycle suivant : elle aurait été publiquement
visible entre les deux. Et un brouillon n'est jamais touché : tant que personne ne l'a
planifié, il n'a pas été relu.

### Lot 8 — la diffusion

`BroadcastAnnouncementCommand`, route `POST /admin/notifications/broadcast`, et un
formulaire séparé de l'envoi nominatif dans la console — un même formulaire avec une case
« à tout le monde » met ces deux gestes à un clic l'un de l'autre.

**Le point central, et il a failli m'échapper : une catégorie qu'on peut couper.**
`NotificationCategories.FromRelatedType` rend `null` pour un type inconnu, et l'appelant
lit ce `null` comme « aucune préférence ne s'applique, on envoie ». Une diffusion
commerciale portant un type non mappé aurait donc traversé TOUTES les coupures de tous
les utilisateurs : le défaut le plus permissif du système appliqué au message le moins
souhaité. D'où la catégorie `announcements`, son mappage, et l'interrupteur ajouté dans
les deux applications mobiles — une coupure que l'écran ne propose pas ne sert à rien.

**L'audience est bornée au module.** Les destinataires sont ceux qui ont enregistré un
appareil, pas « tous les comptes ». Énumérer la table des utilisateurs depuis Notifications
pour leur écrire à tous aurait franchi une frontière de module pour fabriquer une liste de
diffusion, et créé une ligne par compte — y compris pour ceux qui n'ont jamais installé
l'application. Ceux-là voient le bandeau, qui est la même information par un autre chemin.

**Le plafond est explicite et modeste.** Pas de valeur par défaut « tout le monde » :
l'appelant doit dire combien de personnes il accepte de toucher. Le plafond absolu est à
500 parce que l'envoi se fait DANS la requête HTTP de l'administrateur — au-delà, elle
expirerait et il relancerait sans savoir ce qui est parti. Passer cette borne demandera
une file et un service d'arrière-plan, pas une constante qu'on augmente.

CE N'EST PAS UN OUTIL DE PROSPECTION. Rien ne segmente par comportement d'achat, et c'est
délibéré : le jour où ce sera nécessaire, ce sera une décision à prendre avec le texte
d'information des utilisateurs sous les yeux.

### La relecture adversariale, et ce qu'elle a trouvé

Dix défauts, tous corrigés. Les quatre qui comptent :

- **Un test ne compilait plus.** `Notifications.UnitTests` contient un faux dépôt de
  jetons ; l'ajout d'un membre à l'interface le laissait incomplet. La relecture
  précédente avait affirmé qu'il n'y avait « aucun projet de test dans la solution » —
  c'était faux, il y en a trente. Vérifié depuis : aucun faux n'implémente les interfaces
  touchées par les lots 0 et 4.
- **Un interrupteur invisible depuis longtemps.** Sur l'écran des préférences VENDEUR, un
  `if` qui gardait un séparateur disparu gardait en réalité l'interrupteur suivant : la
  première catégorie — « Commandes » — n'était jamais rendue. Défaut antérieur à ce lot,
  trouvé en comparant avec l'écran acheteur, qui avait gardé son séparateur.
- **Une panne du BFF se mettait en cache comme un 404.** La page d'événement appelle
  `notFound()`, et Next met le 404 en cache 60 s : un hoquet transformait une opération
  vivante en « cet événement n'existe pas ». Seul un vrai 404 rend désormais null ; le
  reste lève. La LISTE, elle, continue d'avaler — elle est lue depuis la mise en page, et
  une erreur y ferait tomber toutes les pages du site pour une entrée de menu.
- **Une date affichée un jour trop tôt.** `Intl.DateTimeFormat` sans fuseau prend celui du
  serveur, qui tourne en UTC : une remise expirant à 23 h 30 UTC s'affichait la veille du
  jour où elle finit pour un acheteur béninois. Vérifié après correction — le 25 à 23 h 30
  UTC affiche bien « 26 sept. » à Porto-Novo.

Les six autres : plafond de diffusion ramené de 5 000 à 500, `try/catch` mort et compteur
de push qui comptait toujours juste supprimés, `AsNoTracking` sur la lecture des jetons,
`lower()` inutile retiré et ordre explicite sur l'audience tronquée, et les libellés de
l'écran de préférences du site — qui décrivaient un vocabulaire n'ayant jamais existé
côté serveur, et affichaient donc les clés techniques en anglais.

### À faire à la mise en production

`dotnet build Marketplace.sln` **et `dotnet test`** — c'est le projet de tests qui a
révélé le seul défaut bloquant de ce lot. Aucune migration : lots 6 à 8 ne touchent à
aucun schéma. Vérifier ensuite qu'une campagne planifiée dont l'heure est venue passe
active toute seule dans la minute, et faire une diffusion d'essai à 1 destinataire avant
d'en faire une vraie.
