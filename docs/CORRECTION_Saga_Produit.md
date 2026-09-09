# Correction de la saga de création de produit

**Date** : 9 septembre 2026
**Base** : l'ordre de réparation de `docs/AUDIT_Saga_Produit.md`, appliqué étape par étape.
**Portée** : les douze points de l'ordre de réparation, plus la décision explicite « chaque catégorie définit son propre schéma d'attributs ».
**Vérification** : aucune compilation, aucune exécution — le SDK n'est pas disponible dans cet environnement. Une relecture de compilabilité statique, fichier par fichier, a été menée sur l'intégralité du lot ; elle a trouvé deux défauts, corrigés (détail en fin de document).

---

## En une phrase

L'audit décrivait un parcours plein de **portes ouvertes** : chaque contrôle existait quelque part et manquait ailleurs. La correction pose ces contrôles **sur la notion plutôt que sur la surface** — un droit de vendre, un schéma d'attributs, une clé de stockage, une devise — de sorte qu'aucune surface d'écriture ne puisse les contourner par distraction.

---

## Étape par étape

### 1. §2.1 — les brouillons et les produits rejetés ne sont plus publics

`ICatalogModuleApi` porte désormais **deux** lectures au lieu d'un booléen :

- `GetProductAsync` — INTERNE, tous statuts confondus, pour le vendeur sur sa propre fiche, l'administration, l'indexeur, et les lignes de panier et de commande qui décrivent un article **acheté** et doivent l'afficher même archivé depuis.
- `GetPubliclyVisibleProductAsync` — nulle si le produit n'est pas `Active`.

Deux méthodes plutôt qu'un paramètre : un booléen s'oublie à l'appel, et l'oubli est silencieux ; un nom qui dit ce qu'il garantit ne s'oublie pas.

Les trois surfaces anonymes ont été recâblées : `ClientCatalogEndpoints`, `ClientStorefrontEndpoints`, et surtout `ClientDeepLinkEndpoints` — la page HTML publique `/p/{id}` avec ses balises Open Graph, qui rendait un brouillon partageable et moissonnable.

Le filtre est posé **après** le cache, délibérément : les deux chemins partagent ainsi une seule clé Redis.

### 2. §7.1 et §7.2 — la clé de stockage ne vient plus du client

- `ExternalId` est **retiré** de `AddProductMediaCommand` et des deux DTO d'entrée. Un identifiant fourni par le client ne peut plus devenir une clé de suppression sur le stockage objet.
- `CloudflareR2MediaStorage.NormalizeKey` refuse désormais une URL, un `://`, et tout segment `.` ou `..`. Elle acceptait une URL publique et en extrayait la clé — les URL d'images produit étant publiques, un vendeur effaçait les images d'un autre.
- Nouveau `IMediaUrlPolicy` / `ConfiguredMediaUrlPolicy` : une URL de média doit appartenir à un hôte de stockage configuré, en HTTPS. **Aucun hôte configuré = tout est refusé** — le repli sûr, et non l'inverse.

### 3. §1.1 et §1.5 — le droit de vendre, en un point de passage

`SellerRights` (module Contracts) répond à trois questions et à elles seules :

```csharp
CanSell(status)      // Active ou Pending
CanWithdraw(status)  // tout sauf Suspended
DenialReason(status) // le message que reçoit le vendeur
```

`SellerBff.ResolveSellingSellerAsync` et `ResolvePayableSellerAsync` l'appliquent, et le contrôle passe **avant** celui de propriété. Toutes les surfaces d'écriture vendeur y sont branchées : catalogue, offres, stock, portefeuille, boutique, avis. Les lectures (fulfillment, retours, litiges) restent ouvertes à dessein : un vendeur suspendu doit pouvoir consulter ses commandes en cours, et le compte et le KYB restent accessibles pour ne pas créer de cul-de-sac.

### 4. §3.1 et §3.5 — l'arrondi de l'offre, et la devise validée

`Money` gagne deux membres :

```csharp
public static bool IsSupported(string? currency)                    // la table CONTIENT-elle ce code ?
public static IReadOnlyCollection<string> SupportedCurrencies { get; }
```

`DecimalsFor` conserve son repli à deux décimales : c'est le bon comportement **en aval**, quand un calcul est déjà engagé. `IsSupported` répond à la question **en amont**, au moment où un appelant extérieur choisit la devise — là où un code inconnu ne doit pas être deviné, mais refusé.

Dans `Offer` :

- `ComputeBreakdown` prend la devise et appelle `Money.Round(…, currency)` au lieu de `Math.Round`. Les cinq sites d'appel passent la devise.
- La remise en pourcentage et le montant fixe s'arrondissent de même.
- `Create` refuse une devise non prise en charge (`offers.offer.currency_unsupported`, avec la liste des devises admises) et un prix qui ne tient pas dans sa devise (`offers.offer.price_precision`).
- **`ChangePrice` applique le même contrôle de précision** : refuser 1000,50 XOF à la création et l'admettre à la modification n'aurait rien fermé.
- `CreateOfferCommandValidator` porte enfin une règle sur `Currency`.

Chiffrage rappelé par l'audit, désormais impossible : une offre en euro à 10,40 € net faisait perdre 0,38 € par unité, soit 19 % de la marge plateforme, dans le sens qui la coûte.

### 5. §1.2 et §1.3 — la suspension sort du module, et ne se blanchit plus

`Suspend()` écrivait un statut et rendait la main : aucun événement, donc aucun effet hors de Sellers.

**Domaine.** `Suspend(reason)` refuse une seconde suspension, mémorise `SuspendedOnUtc`, `SuspensionReason`, incrémente `SuspensionCount` — **jamais remis à zéro** — et lève `SellerSuspendedDomainEvent`.

**Le blanchiment est coupé à son premier maillon.** L'enchaînement était : l'admin suspend → le vendeur ferme sa boutique lui-même → il demande la réactivation → l'admin approuve, sanction effacée. `RequestClosure` refuse désormais un compte suspendu. `Activate` aussi : la suspension ne se lève plus par la porte de l'activation.

**`LiftSuspension()`** remplace un `Reactivate()` qui n'avait **aucun appelant** et **aucune garde de statut**. C'est la seule sortie de l'état suspendu ; elle laisse `SuspensionCount` intact.

**Consommateurs.** Catalog dépublie les produits du vendeur (`Draft`, pas `Archived` : une suspension est révocable). La levée ne republie **pas** — republier en masse rendrait visibles les fiches que le vendeur avait lui-même retirées avant la sanction.

**Notifications.** Le vendeur reçoit la suspension **avec son motif**, par notification et par e-mail. Corollaire : le message « Boutique suspendue » qui partait au vendeur ayant fermé lui-même sa boutique est devenu « Boutique fermée » — il annonçait une sanction qui n'avait pas eu lieu pendant que le vrai suspendu ne recevait rien.

**Surfaces.** `POST /admin/sellers/{id}/suspend` prend un motif obligatoire dans le corps ; `POST /admin/sellers/{id}/lift-suspension` est nouvelle. La route API `/api/sellers/admin/{id}/suspend` a été alignée.

### 6. §4.1 — le SKU d'une offre appartient au produit

`CreateOfferCommandHandler` chargeait le produit et ne s'en servait que pour tester la nullité. Il vérifie désormais que le SKU correspond à une variante **de ce produit** (`offers.offer.sku_not_in_product`), et qu'un produit sans variante ne peut pas recevoir d'offre.

Ce n'était pas une incohérence d'affichage : le panier réserve sur `offer.Sku`. L'acheteur commandait la fiche A et le système décrémentait le stock de l'article B.

### 7. §3.2 — les deux champs oubliés du mapper

`OfferModuleApi.Map` s'arrêtait à `ProviderFeeRate` ; `CompareAtAmount` et `DiscountEndsOnUtc`, paramètres optionnels, prenaient toujours leur valeur nulle sans qu'aucun compilateur ne s'en plaigne. Le vendeur finançait une promotion dont l'argument commercial n'atteignait jamais l'acheteur. Deux arguments ajoutés.

### 8. §1.4 — `/api/sellers/*` : le rôle, et le cul-de-sac

**Le rôle.** Nouveau `MapSellerGroup`, qui exige `Seller`. La constante `SellerRole` existait depuis toujours sans aucun usage, et le groupe partait de `MapAuthenticatedGroup` : les cinq hôtes partageant la clé de signature, un jeton d'acheteur ouvrait la surface vendeur.

**La route fautive est supprimée.** `POST /api/sellers/` créait une boutique sans jamais attribuer le rôle. Le compte se retrouvait avec une boutique inaccessible **et** l'impossibilité de repasser par `/seller/auth/verify`, qui refusait en 409 dès qu'une boutique existait. Une fois le rôle exigé, cette route n'avait plus d'appelant possible — qui a le rôle a déjà une boutique.

**Les comptes déjà enfermés sont libérés.** `/seller/auth/verify` lit maintenant le rôle **avant** de décider :

| Boutique existante | Rôle `Seller` | Décision |
|---|---|---|
| oui | présent | 409 — vrai doublon |
| oui | absent | **c'est le cul-de-sac** : on reprend à l'attribution du rôle |
| non | — | création normale |

Le nom de boutique du corps est ignoré dans le cas de réparation : la boutique existe, la renommer ici serait une modification de profil déguisée en inscription.

### 9. §5.1 et §5.6 — l'index qui diverge, et le filet

**§5.1.** `Product.Update()` lève désormais `ProductUpdatedDomainEvent` → `ProductUpdatedIntegrationEvent` → `ProductUpdatedSearchHandler`. Le nom et le slug de l'index étaient figés au jour de la création : un vendeur qui corrigeait le nom de son article restait trouvable sous l'ancien uniquement, et introuvable sous le nouveau. Définitivement.

**§5.6.** `POST /admin/search/reindex` reconstruit l'index depuis le catalogue.

- Pagination **par curseur**, pas par offset : un `Skip(n)` décalerait les pages suivantes dès qu'une ligne serait créée ou supprimée pendant le parcours, donc sauterait des produits en silence. `ProductId` devient comparable pour cela, avec un commentaire disant que cet ordre n'a aucun sens métier.
- Réécrit l'identité (nom, slug, vendeur, catégorie, image, **visibilité**) ; ne touche **ni au prix ni à la note**, qui appartiennent à Offers et Reviews.
- Ne supprime aucun document : un orphelin s'affiche mal, une suppression en masse fondée sur une lecture incomplète viderait la vitrine.
- Corrige au passage §5.2 : la migration de visibilité avait mis **tout** l'existant à visible, brouillons compris, et rien ne pouvait plus corriger ces lignes.

C'est le filet qui rattrape les défauts d'index qu'on n'a pas trouvés.

### 10. §4.5 et §7.7 — les violations d'unicité deviennent des 409

Nouveau `UniqueViolationExceptionHandler`, enregistré sur les cinq hôtes à côté de `ConcurrencyExceptionHandler`. Il remonte la chaîne des causes jusqu'à la `PostgresException`, teste `SQLSTATE 23505`, et nomme le champ concerné à partir du nom de la contrainte.

**Il ne dit jamais quelle valeur est prise, ni par qui.** Le message natif de PostgreSQL contient la ligne en conflit : le renvoyer ferait de ces routes un oracle d'énumération des noms de produits et des SKU des concurrents. Le détail va dans les journaux.

Trois 500 disparaissent : le SKU déjà pris par un autre vendeur, la double création simultanée du même produit (après téléversement des images), et le même schéma sur l'offre et l'article de stock.

### 11. §7.3, §7.4, §7.5 — le dossier vidé, le refus muet, le produit perdu

**§7.3.** `RemoveKybDocument` refuse une pièce **déjà vérifiée**. La règle porte sur la pièce, pas sur le statut de la boutique : une pièce qu'un modérateur a examinée est une décision de conformité, elle appartient au dossier. La pièce non encore vérifiée reste supprimable — c'est le téléversement erroné qu'on corrige.

**§7.4.** `RejectKyb(reason)` exige un motif **dans le domaine**, le mémorise, et lève `SellerKybRejectedDomainEvent`. Le vendeur reçoit le refus et sa raison par notification et par e-mail. Le motif est effacé dès qu'une nouvelle pièce relance la revue — l'afficher pendant la nouvelle revue ferait croire à un second refus. `SellerSummary` expose `KybRejectionReason`, pour que l'écran de compte cesse d'afficher « Rejected » tout court.

**§7.5.** La modération **rejette vers `Draft`**, plus vers `Archived`. « Rejeter » veut dire « pas publiable en l'état », pas « retirer du catalogue » ; le brouillon est exactement cet état, et le vendeur ne peut toujours pas publier lui-même. Le retrait ferme reste disponible par `PATCH /status`. Et le refus d'`Activate` sur un produit archivé dit désormais la sortie au lieu de se contenter d'un 409 muet. La contradiction documentaire « transition terminale » / `Unarchive` quatre lignes plus bas est levée.

### 12. Chaque catégorie définit son propre schéma d'attributs

C'était votre décision explicite, et c'est le seul point de l'ordre de réparation qui ajoute une **fonctionnalité** plutôt qu'un garde-fou.

`Category.AttributeSchema` existait depuis l'origine : une colonne `jsonb`, un champ dans les formulaires d'administration, transporté par les commandes de création et de modification. Quinze occurrences dans le dépôt, **toutes en écriture ou en transport, aucune lecture**. Le champ n'était même pas vérifié comme JSON avant d'atteindre la colonne.

**Le format.** Explicite, et non JSON Schema : le besoin est étroit — une liste de champs typés, requis ou non, avec des valeurs admises. JSON Schema saurait tout exprimer, au prix d'une dépendance, d'un vocabulaire que l'administrateur devrait apprendre, et de messages d'erreur écrits pour des développeurs.

```json
{
  "allowUnknown": false,
  "attributes": [
    { "key": "pointure", "label": "Pointure", "type": "number",
      "required": true, "min": 16, "max": 50 },
    { "key": "couleur",  "label": "Couleur",  "type": "enum",
      "required": true, "values": ["rouge", "bleu", "noir"] },
    { "key": "cuir",     "label": "Cuir véritable", "type": "boolean" },
    { "key": "modele",   "label": "Modèle",   "type": "text", "max": 40 }
  ]
}
```

Types : `text`, `number`, `boolean`, `enum`. Bornes `min`/`max` sur les nombres, longueurs sur les textes, `unit` pour l'affichage.

**Pas d'héritage.** Une catégorie fille ne reprend pas les attributs de son parent — c'est la lecture littérale de votre décision, et elle se défend : un schéma hérité ne se lit nulle part en entier, et le jour où un attribut est ajouté haut dans l'arbre, tous les produits des branches en dessous deviennent invalides d'un coup sans qu'aucun vendeur n'ait rien fait. Le prix est la répétition ; c'est le bon prix.

**Le schéma vide n'interdit rien.** Toutes les catégories existantes portent `{}`. Aucun produit déjà en base ne devient invalide. C'est en renseignant le schéma d'une catégorie qu'un administrateur décide d'y appliquer une contrainte.

**Où il s'applique.** `ProductAttributeGuard`, un point de passage unique injecté par les **trois** handlers qui écrivent des attributs — création simple, création avec images, modification. Le recopier dans les trois aurait reproduit exactement le défaut que l'audit décrit partout ailleurs.

Il fait trois choses :

1. **Il vérifie que la catégorie existe** — `Product.Create` ne contrôlait que `categoryId != Guid.Empty`, et aucun handler ne chargeait la catégorie, faute de clé étrangère entre deux schémas séparés. Un produit pouvait naître sur une catégorie inexistante. Ceci ferme la première moitié de §4.3. Une catégorie **archivée** est refusée ; une catégorie en **brouillon** est acceptée, parce qu'un rayon en préparation est un cas normal.
2. **Il refuse ce que le schéma interdit**, en rassemblant tous les défauts en un seul message — un formulaire qui refuse une erreur à la fois oblige à autant d'allers-retours, et sur une fiche produit un aller-retour veut dire retéléverser les images.
3. **Il normalise les clés** sur celles du schéma. C'est la moitié utile que la seule validation ne fait pas : « Couleur », « couleur » et « COULEUR » donnaient trois clés en base pour une même notion, donc trois facettes de recherche et aucun filtre qui fonctionne.

Dans la création avec images, le contrôle est placé **avant** le téléversement : les images partent au stockage objet avant toute écriture en base et rien ne les supprime si la suite échoue (§7.6). Un refus placé après aurait laissé des objets orphelins facturés.

**Le schéma est validé à sa saisie.** `Category.Create` et `Category.Update` refusent un JSON mal formé, un type inconnu, un `enum` sans valeurs, deux clés identiques à la casse près, des bornes inversées. Avant, une saisie fautive donnait un 500 opaque à l'administrateur — ou passait, et n'échouait qu'au moment où un vendeur créait un produit, très loin de la faute.

**Le vendeur voit ce qu'on attend de lui.** `CategorySummary` porte désormais le schéma sous sa forme lue (clés, libellés, types, valeurs admises, obligatoire) : de quoi dessiner un vrai formulaire au lieu d'un champ « attributs » libre. La projection a été extraite dans `CategoryMapping` — elle était recopiée dans trois fichiers **qui partagent leur clé de cache**, et deux projections divergentes auraient servi des objets différents sous la même clé selon qui a rempli le cache en premier.

---

## Ce qu'il vous reste à lancer

### La migration

Une seule, sur le module Sellers — quatre colonnes, toutes nullables ou à défaut, donc **sans reprise de données** :

```bash
dotnet ef migrations add AddSellerSuspensionMemory \
  -p src/Modules/Sellers/Marketplace.Modules.Sellers.Infrastructure
```

| Colonne | Type | Pourquoi |
|---|---|---|
| `suspended_on_utc` | `timestamp null` | date de la suspension en cours |
| `suspension_reason` | `varchar(1000) null` | motif de la suspension en cours |
| `suspension_count` | `int not null default 0` | mémoire de la sanction, jamais remise à zéro |
| `kyb_rejection_reason` | `varchar(1000) null` | motif du dernier refus de KYB |

Aucune autre migration : `attribute_schema` existait déjà, et rien n'a changé de forme côté Catalog, Offers ou Search.

### La compilation et les tests

```bash
dotnet build
dotnet test
```

Cinq fichiers de tests ont été ajoutés :

| Fichier | Couvre |
|---|---|
| `tests/Offers.UnitTests/ArrondiEtDeviseTests.cs` | §3.1, §3.5 — devise refusée, ventilation entière en XOF, centimes conservés en EUR, précision à la création **et** à la modification, remise arrondie |
| `tests/Sellers.UnitTests/SuspensionTests.cs` | §1.2, §1.3 — l'événement et son motif, la double suspension refusée, les trois portes du blanchiment fermées, la levée qui garde la mémoire |
| `tests/Sellers.UnitTests/KybDossierTests.cs` | §7.3, §7.4 — la pièce validée insupprimable, le motif obligatoire, son effacement à la nouvelle revue |
| `tests/Catalog.UnitTests/ModerationProduitTests.cs` | §7.5 — rejeter, corriger, approuver |
| `tests/Catalog.UnitTests/SchemaAttributsCategorieTests.cs` | le schéma d'attributs : lecture, refus de saisie fautive, contrôle des produits, normalisation des clés |

### Le point à confirmer sur base réelle

`CatalogModuleApi.ListForIndexingAsync` compare `p.Id > new ProductId(cursor)` sur une propriété munie d'un value converter. La traduction en `"id" > @cursor` devrait se faire — EF Core ignore le `Method` de l'expression binaire et applique le converter à la constante — mais je ne peux pas le prouver sans exécution. Si la reconstruction d'index lève un `InvalidOperationException: The LINQ expression could not be translated`, c'est là. Le reste du lot ne dépend pas de ce point.

À noter aussi : `CategorySummary` a gagné deux champs et est **mis en cache**. Les entrées déjà en Redis se désérialiseront avec un schéma vide jusqu'à expiration de leur TTL — sans erreur, mais l'application vendeur ne verra les schémas qu'après ce délai.

---

## Ce que la relecture a rattrapé

Aucun compilateur n'étant disponible, une relecture statique ligne à ligne a été menée sur les dix-huit fichiers nouveaux et les zones modifiées des soixante-dix autres. Deux défauts réels ont été trouvés et corrigés :

1. **Une route devenue impossible.** `/api/sellers/admin/{id}/suspend` continuait d'envoyer `SuspendSellerCommand(id)` sans motif — cela compilait, le paramètre étant optionnel, mais le nouveau validateur exige le motif : la route aurait renvoyé 400 à chaque appel. Elle prend désormais un corps, comme celle de la console.

2. **Un garde-fou qui criait au loup.** Le compteur de pages de la reconstruction d'index concluait à l'emballement si le catalogue tenait en exactement 10 000 pages, alors que le parcours avait réussi. Remplacé par un drapeau posé à la sortie normale.

S'y ajoutent deux nettoyages : un double bloc `<summary>` sur `Offer.ComputeBreakdown`, et les trente glyphes `` restants dans les fichiers touchés.

---

## Ce qui reste ouvert

L'ordre de réparation s'arrêtait à douze points. Ce que l'audit signale et qui n'a pas été traité, par ordre de gravité :

- **§2.2 — la marge de chaque vendeur est publique.** `OfferEndpoints` sérialise `OfferSummary` en `AllowAnonymous` sur deux routes, prix net et barème compris. La protection existe pourtant : `ClientCatalogEndpoints` projette explicitement.
- **§2.3 et §2.4 — le stock est énumérable**, et un vendeur surveille celui de ses concurrents.
- **§3.3 — le retrait de remise relit les taux courants** au lieu des taux figés sur l'offre.
- **§4.2, §4.4 — variante supprimée ou renommée**, catégorie renommée : la descendance et les offres restent orphelines.
- **§5.3, §5.4, §5.5 — l'index et les offres** : le « à partir de » écrasé, la mise en pause invisible, la chaîne rupture → offre indisponible qui n'existe pas.
- **§5.8 — page non bornée** et `ILIKE` non ancré sur une route anonyme.
- **§6 — les modules-décor** : Billing, Recommendations, `ISellerModuleApi.IsActiveSellerAsync`, et les commentaires qui affirment le contraire. Les documenter comme non branchés coûte une heure.
- **§7.8 à §7.12** : le renommage impossible dès qu'un homonyme existe, le slug d'un produit publié réécrit, l'absence de verrou optimiste sur `Offer`, la pagination non bornée côté admin, et l'identifiant client du service média en dur dans le dépôt.

---

*Correction menée sans compilateur ni base de données. Les invariants sont posés par lecture et couverts par des tests unitaires qui n'ont pas été exécutés.*
