# 23 — Documents juridiques versionnés

> Écrit le 19/09/2026, à la suite de la décision de gérer les contenus juridiques
> depuis la console d'administration plutôt qu'en dur dans le frontend.
>
> **Ce document décrit du code qui n'a jamais été compilé.** Aucun SDK .NET n'était
> disponible au moment de l'écrire (api.nuget.org refusé par le proxy). Tout ce qui
> suit a été vérifié par relecture — signatures comparées aux primitives réelles,
> équilibrage des blocs, revue adversariale indépendante — jamais par un compilateur.
> Le premier `dotnet build` reste le seul juge.

---

## 1. Ce qui existait, et pourquoi il fallait changer

La séparation contenu/affichage était déjà en place : `ContentBlock` dans le module
Marketing, `GetLegalDocumentQuery` qui analyse le bloc côté module, `/admin/content` en
CRUD sur le BFF admin, et deux routes de lecture anonymes (`/client/legal/{document}`,
`/seller/legal/{document}`). Le tuyau existait.

Trois défauts, par ordre de gravité.

**`ContentBlock.Update()` écrasait `Content` sur place.** Pas d'historique. Corriger une
virgule dans les CGU effaçait la rédaction que les utilisateurs avaient acceptée, et plus
rien ne permettait de dire ce qu'ils avaient eu sous les yeux.

**Le numéro de version était une chaîne saisie à la main DANS le JSON.** Rien n'empêchait
de publier un texte nouveau sous l'ancien numéro. Personne n'était alors invité à
réaccepter, l'acceptation enregistrée désignait une rédaction disparue, et rien ne le
signalait. L'erreur ne se découvre que le jour du litige.

**`User.AcceptedTermsVersion` : une seule case pour toute la plateforme.** Un vendeur qui
accepte les CGU puis les conditions vendeurs écrasait la trace de la première. Les deux
cases à cocher de l'inscription vendeur étaient littéralement inenregistrables.

---

## 2. Le modèle

```
LegalDocument                     legal_documents
├── Id                            uuid, PK
├── Kind                          Terms | SalesTerms | SellerTerms | Privacy
│                                 | Cookies | Refund | Delivery | LegalNotice
├── Slug                          UNIQUE — l'adresse par laquelle les BFF demandent
├── Title                         modifiable sans créer de version
├── Status                        Draft | Published | Archived
├── CurrentVersionId              raccourci de lecture, PAS la source de vérité
└── Versions ─────────┐

LegalDocumentVersion   ◄──────────┘   legal_document_versions
├── Id                            uuid, PK
├── DocumentId                    FK → legal_documents, CASCADE
├── Version (colonne)             « 1.2 » — UNIQUE par document
├── Content                       jsonb : { lastUpdated, company, sections[] }
├── Status                        Draft | Published | Archived
├── EffectiveFromUtc              date à laquelle le texte FAIT FOI
├── PublishedOnUtc
├── PublishedBy                   l'administrateur, pris dans le JWT
└── CreatedOnUtc

LegalAcceptance                   legal_acceptances
├── Id                            uuid, PK
├── UserId                        identifiant NU — aucune FK inter-schéma
├── DocumentId
├── VersionId                     ◄── tout l'objet de la table
├── AcceptedOnUtc
└── Context                       « consent_gate_seller », « signup_seller »…
                                  UNIQUE (UserId, VersionId)
```

### Les quatre invariants, et où ils se défendent

| Invariant | Défendu par |
|---|---|
| Un seul brouillon par document | `LegalDocument.SaveDraft` (écrit toujours au même endroit) |
| Un numéro jamais réemployé | l'agrégat **et** l'index unique `(DocumentId, Version)` |
| Une publication ne recule jamais | `LegalDocument.PublishDraft` compare `LegalVersionNumber` |
| Une acceptation par personne et par version | le handler **et** l'index unique `(UserId, VersionId)` |

Les deux invariants doublés le sont sciemment : la garde applicative lit avant d'écrire,
ce qui laisse passer deux appels simultanés — un double clic sur « J'accepte » suffit.

### Trois choix qui méritent d'être justifiés

**Le numéro est stocké en texte, et aucune requête ne trie dessus.** « 1.10 » est
postérieur à « 1.9 » mais lui est inférieur en tri alphabétique. La comparaison se fait
dans le domaine, sur deux entiers. Les listes trient sur `CreatedOnUtc`.

**`EffectiveFromUtc` existe pour qu'on puisse publier le 1er un texte applicable le 15.**
La lecture publique sert la version publiée dont la date d'effet est passée — pas
forcément `CurrentVersionId`, qu'un simple pointeur ne saurait exprimer.

**Aucune clé étrangère depuis `legal_acceptances`.** Vers `identity.users` : la règle du
dépôt interdit une contrainte inter-schéma. Vers `legal_document_versions` : une preuve de
consentement ne doit pas pouvoir être emportée par une cascade. Si un document est
supprimé, ces lignes survivent et deviennent inexploitables bruyamment, plutôt que de
disparaître en silence.

---

## 3. La validation a changé de moment

Elle se faisait **à la lecture**, dans le handler qui sert le document. Un JSON mal formé
saisi dans la console s'enregistrait sans broncher et n'était découvert que par un
acheteur au moment d'accepter — qui voyait l'écran retomber sur la copie embarquée, sans
que personne côté plateforme n'apprenne quoi que ce soit.

Elle se fait maintenant **à l'écriture** (`LegalContent.Serialiser`). Un texte refusé au
clic sur « Enregistrer » se corrige en dix secondes.

Le format reste **structuré** — `{ titre, paragraphes[] }` — et les paragraphes acceptent
du **Markdown simple**. Pas de HTML : le rendu mobile est un widget Flutter qui parcourt
cette structure, et un éditeur WYSIWYG produirait des balises qu'il ne sait pas afficher
et qu'il faudrait assainir côté web sous peine d'injection.

---

## 4. La bascule ne casse rien

`GetLegalDocumentQuery` — la requête que les deux BFF appellent déjà — consulte
maintenant **deux sources, dans cet ordre** :

1. le document versionné, si son `Slug` correspond à l'emplacement demandé ;
2. à défaut, l'ancien `ContentBlock`.

Les `Slug` reprennent volontairement les emplacements existants
(`legal_terms_customer`, `legal_privacy_customer`, `legal_terms_seller`,
`legal_privacy_seller`) : **aucun appel existant ne change**. La reprise se fait document
par document, sans fenêtre pendant laquelle rien n'est servi.

L'ordre compte : dès qu'un document versionné existe pour une adresse, c'est lui qui fait
foi. Sinon un ancien bloc oublié continuerait de servir un texte abrogé.

### L'acceptation : deux écritures, et la réponse dit laquelle a pris

`POST /account/accept-terms` écrit désormais aux deux endroits et répond
`{ version, recorded }`.

`recorded: false` signifie que la version présentée ne correspond à aucune version publiée
— **le cas normal tant que le document n'est pas dans le CMS** : l'application affiche
alors sa copie embarquée. On ne fabrique pas de version fantôme pour faire réussir
l'appel : une preuve qui pointe vers un texte que personne ne peut relire est pire qu'une
absence de preuve, parce qu'elle en a l'apparence.

**Un `false` ne bloque jamais la personne.** C'est un signal pour la plateforme.

Les deux colonnes `User.AcceptedTermsVersion` / `AcceptedTermsOnUtc` restent écrites : les
portillons de consentement déjà déployés les lisent, et les retirer maintenant ferait
redemander l'accord à tout le monde. La migration **ne les reprend pas** non plus — la
colonne ne dit pas QUEL document a été accepté, et deviner reviendrait à fabriquer des
preuves de consentement.

---

## 5. Les routes

| Méthode | Route | Effet |
|---|---|---|
| `GET` | `/admin/legal/` | les documents et leur historique, sans les textes |
| `POST` | `/admin/legal/` | crée un document (aucune version) |
| `PUT` | `/admin/legal/{id}/title` | renomme — n'affecte aucune version |
| `GET` | `/admin/legal/{id}/version` | le brouillon, ou à défaut le texte en vigueur |
| `GET` | `/admin/legal/{id}/versions/{versionId}` | une version précise, avec son texte |
| `PUT` | `/admin/legal/{id}/draft` | écrit ou réécrit le brouillon |
| `POST` | `/admin/legal/{id}/publish` | publie — `publishedBy` vient du JWT |
| `POST` | `/admin/legal/{id}/archive` | retire le document, l'historique reste |
| `GET` | `/admin/legal/acceptances/{userId}` | ce qu'une personne a accepté |

Groupe **séparé** de `/admin/content`, et ce n'est pas du rangement. Un bloc éditorial se
modifie et se supprime — personne ne signe une bannière. Un document juridique n'a pas ces
gestes-là : il a « écrire un brouillon », « publier une version », « archiver ». Deux jeux
de routes distincts rendent l'erreur impossible à commettre par inadvertance.

---

## 6. Ordre de déploiement

1. `dotnet build Marketplace.sln` — **la première vraie vérification de tout ce code.**
2. `dotnet ef database update` sur le schéma `marketing`
   (migration `20260919120000_AddLegalDocuments`).
   *Contrôle utile :* un `dotnet ef migrations add Verification` juste après doit produire
   une migration **vide**. Si elle ne l'est pas, l'instantané écrit à la main diverge du
   modèle, et il faut le corriger avant d'aller plus loin.
3. Déployer le monolithe et les trois BFF.
4. Créer les documents (`POST /admin/legal/`), avec ces `Slug` pour les quatre qui
   existent déjà côté surfaces :
   `legal_terms_customer`, `legal_privacy_customer`,
   `legal_terms_seller`, `legal_privacy_seller`.
5. Reprendre les textes de `docs/legal/*.json` en brouillon, puis publier.
   Le numéro de version doit être **supérieur** à celui que porte la copie embarquée des
   applications, sinon personne ne sera invité à réaccepter.
6. Vérifier que `/seller/legal/terms` et `/client/legal/terms` répondent 200 — c'est aussi
   ce qui ferme les 404 de la console vendeur et fait enfin se déclencher le portillon de
   consentement web.

---

## 7. Ce qui reste à faire

- ~~L'écran « Contenus & juridique » de la console admin.~~ **Fait** :
  `hba-express-web/apps/admin/src/app/(admin)/legal/`. Liste des documents, éditeur de
  sections, prévisualisation, publication avec date d'effet, historique en lecture seule.
  L'espace `legal` est ouvert dans la passerelle admin et couvert par
  `npm run verify:espaces`.

  Il n'a **ni « Éditer » ni « Supprimer »** sur une version : les seuls gestes sont
  « enregistrer le brouillon », « publier » et « archiver ». C'est ce qui distingue cet
  écran de celui des bannières, juste à côté.

  Deux détails qui ont leur raison d'être :
  - **« Publier » enregistre d'abord.** Le serveur publie le brouillon ENREGISTRÉ, pas ce
    qui est à l'écran. Sans cet enregistrement préalable, publier après une dernière
    retouche publierait le texte d'avant — et l'écran montrerait la correction.
  - **La prévisualisation ne produit jamais de HTML.** Elle construit des nœuds React, que
    React échappe. Il n'existe aucun chemin par lequel une balise saisie dans le champ
    devienne une balise à l'écran. Voir `legal/markdown-simple.tsx`.
- **Les mentions légales.** Le type `LegalNotice` existe, mais RCCM, IFU, siège et
  directeur de publication ne changent pratiquement jamais et doivent être exacts : les
  tenir dans la configuration du déploiement reste défendable. Ce qui ne l'est pas, c'est
  de les avoir aux deux endroits à la fois. À trancher.
- **La FAQ, l'aide et « À propos »** restent des `ContentBlock` non versionnés — décision
  prise : personne ne signe une FAQ.
- **Retirer l'ancien repli** de `GetLegalDocumentQuery` une fois les quatre documents
  repris, et à terme les colonnes `AcceptedTermsVersion` / `AcceptedTermsOnUtc` une fois
  que tous les binaires déployés lisent la nouvelle réponse.
