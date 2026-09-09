# Audit de la saga de création de produit

**Date** : 9 septembre 2026
**Périmètre** : le parcours vendeur complet — inscription, boutique, KYB, produit, variantes, médias, offre, prix, stock initial, mise en visibilité.
**Méthode** : quatre segments menés en parallèle, puis recoupement. Aucune compilation, aucune exécution. Tous les `grep` excluent `bin/` et `obj/`. Les neuf constats les plus graves ont été **revérifiés à la main** ; ils sont marqués `[revérifié]`.
**Base** : commit `9dc7f46`, plus les 99 fichiers non commités de la correction du parcours d'achat. Ce périmètre-ci n'a pas été touché par cette correction.

---

## Ce que l'audit dit en une phrase

Le parcours d'achat était un problème de **chemins d'erreur** : le nominal marchait, les compensations manquaient. Le parcours de création est un problème de **portes ouvertes** : chaque contrôle existe quelque part et manque ailleurs, et la barrière de modération protège la vitrine mais pas l'accès.

Trois motifs se répètent et expliquent presque tous les défauts qui suivent :

1. **Le contrôle est posé sur une surface, pas sur la notion.** Le droit de vendre est vérifié à un seul endroit sur les dix qui en dépendent. Le statut du produit filtre l'index mais aucune lecture directe. La propriété d'un média est vérifiée sur le produit, pas sur le fichier.
2. **Un identifiant fourni par le client sert de clé serveur.** L'`ExternalId` d'un média devient une clé de suppression sur le stockage objet ; la devise d'une offre pilote un calcul monétaire sans jamais être validée.
3. **Des modules entiers sont documentés comme branchés et ne le sont pas.** Billing, Recommendations, `AttributeSchema`, la chaîne « rupture → offre indisponible » : quatre appareillages complets, quatre fois zéro appelant, et à chaque fois un commentaire qui affirme le contraire.

---

## 1. Le droit de vendre

### 1.1 Il n'est vérifié qu'à un seul endroit du dépôt `[revérifié]`

Le seul contrôle de statut vendeur est `CreateOfferCommandHandler.cs:57` :

```csharp
if (seller.Status is not ("Active" or "Pending"))
    return Error.Forbidden("offers.offer.seller_inactive", …);
```

`seller.Status` n'apparaît **nulle part** dans les quatre BFF, sinon en affichage. Absent de : la création de produit (`SellerCatalogEndpoints.cs:140`), le changement de prix, l'application et le retrait de remise, le changement de statut d'offre (`SellerOfferEndpoints.cs:215-233` — `GuardAsync` ne vérifie que la propriété), et **la demande de retrait de fonds** (`SellerWalletEndpoints.cs:79`).

`SellerBff.ResolveSellerAsync:89-98` renvoie la boutique quel que soit son statut. Le seul garde-fou de la surface est le rôle `Seller`.

**Conséquence.** Un vendeur suspendu ne peut pas créer une offre *nouvelle*, mais il peut republier ses offres en pause, baisser ses prix, appliquer des remises, modifier ses fiches, en créer d'autres, et demander un virement. Le contrôle est sur la porte de service, pas sur la porte d'entrée.

### 1.2 `Suspend()` n'émet aucun événement : la suspension ne sort jamais du module `[revérifié]`

```csharp
// Seller.cs:224
public Result Suspend()
{
    Status = SellerStatus.Suspended;
    return Result.Success();
}
```

Aucun `Raise`. Et il n'existe aucun `SellerSuspendedIntegrationEvent` — les contrats ne déclarent que Registered / Activated / Closed / Reactivated / Deleted. Catalog n'écoute que `SellerClosed` et `SellerDeleted`.

**Conséquence.** Suspendre un vendeur ne dépublie aucun produit, ne désactive aucune offre, ne prévient personne. Sa vitrine reste servie, ses offres restent dans les fiches produit. La sanction est purement cosmétique côté acheteur — et le vendeur, lui, n'est même pas notifié.

### 1.3 Le blanchiment de suspension

Enchaînement entièrement praticable, chaque étape passant sa propre garde :

| Étape | Garde |
|---|---|
| L'admin suspend | aucune |
| Le vendeur ferme sa boutique (`RequestClosure`, `Seller.cs:247`) | refuse `Closed` et `PendingReactivation` — **un suspendu passe** |
| Il demande la réactivation | exige `Closed` — il l'est |
| L'admin « réactive » (`ApproveReactivation`, `Seller.cs:276`) | exige `PendingReactivation or Closed` + KYB vérifié — **rien sur l'historique de suspension** |

`Status` est un champ unique, écrasé : l'agrégat a perdu la mémoire de la sanction. Un vendeur suspendu pour fraude se présente à l'admin comme une banale demande de réouverture.

Aggravant : le vendeur qui ferme lui-même reçoit, par push **et** e-mail, un message intitulé « Boutique suspendue » (`SellerLifecycleNotificationHandlers.cs:51-66`) — l'annonce d'une sanction qu'il n'a pas subie, pendant que le vrai suspendu ne reçoit rien.

### 1.4 `/api/sellers/*` : surface parallèle sans rôle, et cul-de-sac permanent

`SellerEndpoints.cs:36` monte le groupe avec `MapAuthenticatedGroup` — `RequireAuthorization()` **sans rôle**. Le même fichier déclare `SellerRole = "Seller"` (ligne 30), constante **sans aucun usage** dans tout le dépôt. Les cinq hôtes partagent émetteur, audience et clé de signature : un jeton acheteur ouvre donc `/api/sellers/`.

**Conséquence 1.** N'importe quel acheteur à e-mail vérifié crée une boutique par `POST /api/sellers/`, en contournant `/seller/auth/verify` — donc sans jamais recevoir le rôle `Seller`. Squat de noms de boutique inclus.

**Conséquence 2, pire.** Ce compte a une boutique mais pas le rôle. Il ne peut pas entrer dans l'app vendeur, et il ne peut plus repasser par le seul chemin qui attribue le rôle : `SellerRegistrationEndpoints.cs:383-390` renvoie 409 `already_seller` **avant** l'étape d'attribution. Compte définitivement enfermé, seule sortie par une route admin.

### 1.5 Aucun contrôle de statut sur toute la surface catalogue

Corollaire de §1.1, mais il mérite d'être isolé : une boutique `Pending` (KYB non validé), `Suspended` ou `Closed` conserve la **totalité** des droits d'écriture sur son catalogue. Elle peut recréer immédiatement les fiches que l'événement de fermeture vient de retirer. La sanction est annulable par sa cible.

---

## 2. Ce qui est visible et ne devrait pas l'être

### 2.1 Les brouillons et les produits rejetés sont publics `[revérifié]`

`CatalogModuleApi.GetProductAsync` ne filtre **aucun** statut :

```csharp
var product = await _dbContext.Products
    .AsNoTracking().Include(p => p.Variants).Include(p => p.Media)
    .FirstOrDefaultAsync(p => p.Id == id, ct);
```

`product.Status` n'est testé nulle part dans `src/Bff`. Trois surfaces anonymes l'appellent tel quel :

- `ClientCatalogEndpoints.cs:38` — la fiche complète, offres et note comprises
- `ClientStorefrontEndpoints.cs:68` — le comparateur
- `ClientDeepLinkEndpoints.cs:123` — **la page HTML publique `/p/{id}`, avec balises `og:title`, `og:description`, `og:image`**

Le garde-fou de modération existe pourtant et est explicite : `SellerCatalogEndpoints.cs:277-281` refuse en 403 qu'un vendeur publie lui-même.

**Conséquence.** La validation admin ne protège que l'indexation, pas l'accès. Un produit jamais validé, ou **rejeté par un modérateur** (le rejet écrit `Archived`), reste intégralement servi à tout anonyme connaissant l'identifiant — et partageable, et récupérable par un crawler via les OG tags. `ProductSummary` expose même `Status` : le client reçoit la preuve que la fiche n'aurait pas dû lui être servie.

Le commentaire de `ClientDeepLinkEndpoints.cs:131-132` promet « Produit introuvable ou **retiré** : page générique ». Il n'y a aucun test de statut.

### 2.2 La marge de chaque vendeur est publique `[revérifié]`

`OfferSummary` porte `SellerPrice`, `CommissionAmount`, `ProviderFeeAmount`, `CommissionRate`, `ProviderFeeRate`. Deux routes le sérialisent tel quel sans authentification :

```csharp
// OfferEndpoints.cs:22-23 — AllowAnonymous prime sur le MapAdminGroup du groupe
group.MapGet("/{id:guid}", GetAsync).AllowAnonymous();
group.MapGet("/by-product/{productId:guid}", ListByProductAsync).AllowAnonymous();
```

Plus `ClientStorefrontEndpoints.cs:23` (`/compare`), qui renvoie la liste brute.

**Conséquence.** N'importe qui lit le prix net et le barème appliqué, offre par offre, vendeur par vendeur. À noter : `ClientCatalogEndpoints.cs:56-70` projette explicitement et ne fuit rien — la protection existe, deux routes la contournent.

### 2.3 Le stock exact de tout le catalogue est énumérable

`InventoryEndpoints.cs:36-37` : `GET /api/inventory/availability/{sku}` est `AllowAnonymous`, monté hors du groupe admin. Les SKU sont publics côté acheteur.

### 2.4 Un vendeur surveille le stock de ses concurrents

`SellerInventoryEndpoints.cs:217-234` vérifie que le vendeur propose bien ce SKU, puis renvoie la disponibilité **toutes localisations et tous vendeurs confondus** (`InventoryModuleApi.cs:61-67`, aucun filtre de localisation). Comme deux vendeurs de la même variante partagent le SKU, le filtre ne protège rien. Le commentaire des lignes 213-215 décrit exactement la protection qu'il ne fournit pas.

---

## 3. Le prix

### 3.1 Devise non validée et arrondi à l'entier codé en dur `[revérifié]`

```csharp
// Offer.cs:331-338
var commissionAmount = Math.Round(sellerPrice * commissionRate);
var providerFeeAmount = Math.Round(sellerPrice * providerFeeRate);
```

`Math.Round(x)` arrondit à **zéro décimale, quelle que soit la devise** — alors que `Money.Round(amount, currency)` existe désormais et que sa documentation dit être « la primitive que doivent appeler tous les calculs monétaires du dépôt ». Et la devise vient du client sans aucun contrôle : `CreateOfferCommandValidator` ne contient **aucune règle** sur `Currency`.

**Chiffrage** — offre en EUR à 10,40 € net, barème 10 % / 5 % :

| | Calcul | Résultat |
|---|---|---|
| Offre (`Math.Round`) | commission `Round(1,04)` = 1,00 ; frais `Round(0,52)` = 1,00 | prix acheteur **12,40 €** |
| Settlement (`GrossSplit`, arrondi correct) | commission 1,08 ; frais 0,54 | net vendeur **10,78 €** |

Le vendeur a fixé 10,40 € et touche 10,78 €. La plateforme a facturé 2,00 € de majoration et n'en garde que 1,62 € : **0,38 € perdus par unité, soit 19 % de sa propre marge**, dans le sens qui la coûte.

### 3.2 Le prix barré n'arrive jamais à l'acheteur `[revérifié]`

`OfferModuleApi.Map` construit un `OfferSummary` avec dix-sept arguments et s'arrête à `ProviderFeeRate`. Les deux derniers paramètres du record — `CompareAtAmount` et `DiscountEndsOnUtc` — prennent donc **toujours leur valeur par défaut nulle**. (`OfferMapper.ToSummary`, qui les renseigne bien, ne sert que le BFF vendeur.)

Or les trois surfaces acheteur lisent ce champ : `ClientCatalogEndpoints.cs:68`, `ClientBff.cs:62`, `ClientDiscoveryEndpoints.cs:153`.

**Conséquence.** La remise s'applique — le prix de base la porte déjà — mais **aucun badge « en promo », aucun prix barré n'apparaît jamais**. Le vendeur finance une promotion dont l'argument commercial n'est jamais affiché. Le commentaire `Offer.cs:105` décrit un chemin qui n'existe pas.

### 3.3 Le retrait de remise relit les taux courants

`RemoveOfferDiscountCommandHandler.cs:30` et `ExpireOfferDiscountsCommandHandler.cs:29` passent `_pricingOptions.PlatformCommissionRate` au lieu des taux figés sur l'offre — exactement ce que la migration `FreezePricingRatesOnOffer` interdit dans son propre commentaire.

**Conséquence.** Offre à 10 000 net, prix acheteur 11 500. Promotion de sept jours. La plateforme passe à 12 % au troisième jour. À l'expiration, le service restaure **11 700** au lieu de 11 500 : le vendeur n'a rien demandé, et le prix barré qu'il a affiché toute la semaine est désormais *inférieur* au prix « d'origine » restauré.

### 3.4 Le module Billing n'a aucun appelant

`ICommissionModuleApi` se documente : « Settlement l'appelle pour connaître la commission prélevée ». Grep sur `src` et `tests` : déclaration, implémentation, enregistrement DI. **Aucune injection.** Settlement calcule via les taux figés sur la ligne de commande.

**Conséquence.** Tout l'appareillage `CommissionRule` — scopes vendeur et catégorie, frais fixe, plancher et plafond, date d'effet, `CommissionResolver`, les écrans d'administration — n'influence **ni le prix payé ni le net reversé**. Le seul taux qui compte est le global `PricingOptions.PlatformCommissionRate`. Un administrateur qui négocie 5 % avec un vendeur stratégique le saisit, le voit persisté, et le vendeur continue d'être ponctionné à 10 %.

### 3.5 `SellerPrice` non contraint à la précision de sa devise

Le validateur exige seulement `GreaterThan(0)`. Un prix de 1000,50 XOF est accepté : l'offre publie alors simultanément `sellerPrice: 1000.50`, `commissionAmount: 100`, `providerFeeAmount: 50` et `productPrice: 1150` — une addition fausse dans une seule et même réponse.

---

## 4. Les identifiants qui ne se correspondent pas

### 4.1 Rien ne vérifie que le SKU d'une offre appartient au produit `[revérifié]`

`CreateOfferCommandHandler.cs:41-99` charge le produit, s'en sert **uniquement pour tester la nullité**, puis crée l'offre sur `command.Sku`. `product.Variants` — qui porte les SKU réels — n'est jamais consulté. Le BFF ne le fait pas non plus.

**Conséquence.** Une offre peut naître sur un SKU qui n'appartient à aucune variante de ce produit, ou à une variante d'un **autre** produit. Le panier réserve ensuite sur `offer.Sku` : l'acheteur commande la fiche A et le système décrémente le stock de l'article B.

### 4.2 Supprimer ou renommer une variante laisse l'offre et le stock orphelins

`RemoveProductVariantCommand` retire la variante sans consulter Offers ni Inventory. Le garde-fou existe pourtant sur la suppression **du produit** (409 si des offres subsistent) — il n'a pas été posé sur la variante. Même effet avec `UpdateProductVariantCommand`, qui change le SKU sans rien propager.

### 4.3 La catégorie n'est jamais vérifiée, et sa suppression n'est pas répercutée

`Product.Create` ne vérifie que `categoryId != Guid.Empty` ; aucun handler ne charge la catégorie (schémas séparés, donc pas de clé étrangère). `DeleteCategoryCommandHandler` supprime sans regarder ni descendants ni produits rattachés.

**Conséquence.** Un produit peut naître sur une catégorie inexistante, en brouillon ou archivée — le sélecteur vendeur renvoie d'ailleurs *toutes* les catégories, tous statuts confondus. Après suppression d'une catégorie, ses produits pointent dans le vide : ils disparaissent de la navigation par rayon tout en restant actifs, sans aucun signal au vendeur.

### 4.4 Renommer une catégorie casse le chemin de sa descendance

`UpdateCategoryCommandHandler` recalcule `Path` pour la seule catégorie modifiée ; le domaine l'assume en commentaire. Renommer « Chiens » en « Canidés » donne `/animaux/canides` au parent, tandis que l'enfant garde `/animaux/chiens/alimentation`. L'index unique portant sur `Path`, le chemin `/animaux/chiens` devient **libre** — une nouvelle catégorie peut le reprendre et capturer silencieusement la descendance de l'ancienne.

### 4.5 SKU : unicité vérifiée par produit, contrainte globale en base → 500

Le domaine ne vérifie l'unicité qu'au sein du produit ; la base l'impose globalement (`ProductVariantConfiguration.cs:174`). Et **rien ne rattrape la violation** : grep de `DbUpdateException|PostgresException|23505` sur tout le dépôt — aucun résultat. Le seul gestionnaire d'exception enregistré ne traite que la concurrence.

**Conséquence.** Un vendeur qui saisit un SKU déjà pris par un autre vendeur reçoit un **500** au lieu d'un 409 exploitable. La variante n'est jamais créée, le produit reste invendable, et le message ne dit pas quoi corriger. Même schéma sur la création d'offre et d'article de stock.

### 4.6 La disponibilité est agrégée sur tous les vendeurs, la réservation non

`IsInStockAsync` filtre sur le SKU sans localisation ; `TryReserveAsync` est correctement scopé. Un acheteur ajoute donc au panier une offre dont le vendeur n'a rien, parce qu'un **concurrent** a du stock — et l'échec n'arrive qu'au checkout, ou jamais si ce vendeur n'a aucun article de stock (le SKU « non suivi » est réputé vendable).

---

## 5. L'index qui diverge, sans retour

Le moteur de recherche n'est ni Meilisearch ni Elasticsearch ni le full-text Postgres : c'est une table `search.documents` interrogée en `ILIKE '%…%'`.

### 5.1 Modifier un produit ne réindexe jamais rien `[revérifié]`

`Product.Update()` ne lève aucun événement — contrairement à `Activate`, `Archive` et `Unpublish`, qui lèvent tous. Et il n'existe **aucun** `ProductUpdatedIntegrationEvent` dans les contrats.

**Conséquence.** Le nom et le slug de l'index sont figés au jour de la création. Un vendeur qui corrige le nom de son article reste trouvable **sous l'ancien nom uniquement**, et introuvable sous le nouveau — définitivement : un read model n'a pas de durée de vie. Le slug indexé, renvoyé tel quel dans les résultats et servant la navigation, diverge de celui de la base.

### 5.2 La migration de visibilité a rendu visible tout l'existant, brouillons compris

```sql
-- 20250628000000_AddSearchDocumentVisibility.cs:24
UPDATE search.documents SET "IsVisible" = true;
```

Rien ne corrigera ces lignes : la visibilité n'est mise à jour que sur un changement de statut, qu'un produit resté en brouillon n'émettra jamais.

### 5.3 Un changement de prix écrase le « à partir de » multi-vendeurs

`SearchDocument.SetOfferPrice` force `MinPrice` sans comparer aux autres offres. Vendeur A à 6 500, vendeur B à 8 000 ; B monte à 12 000 → le document affiche 12 000 pour un article achetable à 6 500. Le prix est faux à l'affichage **et dans le tri par prix**. Le chemin `OfferDeleted`, lui, calcule correctement le minimum restant : l'asymétrie est dans le code, pas dans l'intention.

### 5.4 Mettre une offre en pause ou en rupture est invisible pour l'index

`Activate`, `Pause` et `MarkOutOfStock` ne lèvent aucun événement, et le document d'index ne porte aucune notion de statut d'offre. Un vendeur qui met sa seule offre en rupture laisse le produit en vitrine, à son prix, en tête du tri par prix.

### 5.5 La chaîne « rupture → offre indisponible » n'existe pas

`StockDepletedIntegrationEvent` se documente : « consommé par Offers pour passer l'offre `OutOfStock`, Search… ». Son unique consommateur est un handler de notification. `Offer.MarkOutOfStock()` n'a qu'un appelant : une action **manuelle** du vendeur.

### 5.6 L'index ne peut pas être reconstruit

Grep de `reindex|rebuild|backfill` sur `src`, `tests` et `docs` : trois occurrences, toutes des commentaires. Aucune commande, aucun endpoint, aucun service de fond.

**Conséquence.** Toute divergence — §5.1 à §5.5, un message parti en lettre morte, un `OUTBOX_ENABLED=false` posé par erreur — est **définitive**. Le rejeu unitaire d'une lettre morte ne sert à rien quand l'événement n'a jamais été produit, ce qui est précisément le cas de §5.1 et §5.4.

### 5.7 Un `DbContext` partagé par les cinquante messages d'un lot d'outbox

`OutboxProcessor` crée une seule portée pour tout le lot, donc une seule instance de `SearchDbContext`. Un `SaveChanges` en échec sur le message *n* laisse l'entité suivie ; le message *n+1* repousse l'écriture empoisonnée et échoue à son tour, en cascade. Un seul document en défaut fait incrémenter le compteur de tentatives de tout le lot — le chemin le plus court vers la mise en lettre morte de masse que la politique de rejeu prétend éviter.

### 5.8 Page non bornée, et `ILIKE` non ancré `[revérifié]`

```csharp
var page = query.Page < 1 ? 1 : query.Page;   // aucune borne haute
…
.Skip((criteria.Page - 1) * criteria.PageSize)
```

L'arithmétique est *unchecked* : `?page=2000000000&pageSize=100` déborde l'`int`, produit un décalage négatif et un **500 sur une route anonyme**. Le dépôt possède pourtant un `PageRequest.Normalize`, utilisé ailleurs.

Et le motif `%texte%` interdit l'usage du B-tree posé sur `Name` : **chaque recherche anonyme est un parcours séquentiel complet**, sans cache. Il n'y a pas d'injection SQL — le paramétrage est correct — mais les jokers ne sont pas échappés : `?q=%` renvoie tout le catalogue, et une chaîne comme `%a%a%a%a%a%` provoque un backtracking coûteux ligne à ligne.

---

## 6. Les modules-décor

Quatre appareillages complets, présentés comme opérationnels, sans aucun consommateur.

- **Billing** — §3.4. Les règles de commission n'influencent rien.
- **Recommendations** — aucun handler d'événement, aucun service de fond. Les routes `/client/products/{id}/related` et `/client/recommendations` renvoient vide tant qu'un administrateur n'a pas saisi les listes à la main. Quatre textes affirment « rafraîchi en arrière-plan » ou « piloté par signaux d'achat/avis ». Et `Recommendation.Refresh` ne vérifie ni l'existence, ni le statut, ni le vendeur des produits recommandés : le jour où le module sera alimenté, il ressortira des produits archivés sur une surface anonyme.
- **`AttributeSchema`** — quinze occurrences, **toutes** en écriture ou en transport. Aucune lecture, aucune validation. Les attributs dynamiques du produit ne sont contraints par rien ; deux vendeurs de la même catégorie livrent des clés incompatibles, et les facettes ne peuvent s'appuyer sur aucune garantie. Le champ n'est même pas validé comme JSON avant d'atteindre une colonne `jsonb` — une saisie admin fautive donne un 500 opaque.
- **`ISellerModuleApi.IsActiveSellerAsync`** — zéro appelant, avec deux commentaires qui affirment que Catalog s'en sert « avant d'accepter la création d'un produit ». Catalog n'a jamais eu la moindre autorisation vendeur.

S'y ajoutent `CategoryCreatedIntegrationEvent` et `BrandCreatedIntegrationEvent`, produits à chaque création et consommés par personne, et `ProductGroupId` — champ persisté, indexé, exposé en écriture au vendeur sans validation, utilisé par aucune requête.

---

## 7. Sécurité et robustesse

### 7.1 Un vendeur peut effacer les images d'un autre `[revérifié]`

L'`ExternalId` d'un média est **fourni par le client** (`MediaRequest`, `SellerCatalogEndpoints.cs:485`), n'est validé par rien, et sert ensuite de clé de suppression sur le stockage objet :

```csharp
// RemoveProductMediaCommand.cs:45
var deletion = await _mediaStorage.DeleteAsync(removed.Value.ExternalId ?? string.Empty, ct);
```

```csharp
// CloudflareR2MediaStorage.cs:174-182
private string NormalizeKey(string externalIdOrUrl)
{
    if (!externalIdOrUrl.StartsWith("http", …)) return externalIdOrUrl.TrimStart('/');
    return new Uri(externalIdOrUrl).AbsolutePath.TrimStart('/');
}
```

`NormalizeKey` accepte **une URL publique** et en extrait la clé. Les URL des images produit sont publiques.

**Chaîne d'exploitation.** Le vendeur A lit l'URL d'une image du vendeur B → `POST /seller/products/{sonPropreProduit}/media` avec `externalId` = cette URL (le contrôle de propriété passe : c'est bien son produit) → `DELETE …/media/{mediaId}` → suppression de l'objet de B. La victime voit des images mortes, sans trace ni erreur.

Le second adaptateur est exposé au même vecteur, avec l'identifiant client de la **plateforme entière** en en-tête : la suppression n'est bornée par aucun locataire.

### 7.2 URL de média entièrement choisie par le client

`AddMediaAsync` accepte `request.Url` tel quel : ni schéma, ni hôte, ni appartenance au domaine de stockage. Un vendeur peut pointer l'image principale de sa fiche vers n'importe quel hôte — pixel de traçage relevant l'IP de chaque acheteur, contenu changé après modération, `data:` URI. L'excellente validation par octets d'en-tête est intégralement contournée par cette route, qui ne téléverse rien.

### 7.3 Un KYB validé survit à la suppression de toutes ses pièces

`RemoveKybDocument` ne touche pas `KybStatus`. Téléverser une pièce, faire valider, supprimer la pièce ⇒ boutique `Verified` avec **zéro justificatif en base**, activable et payable. Le dossier de conformité peut être vidé par son propre titulaire après validation, sans trace ni nouvelle revue.

### 7.4 Un rejet de KYB sans motif ni notification

`RejectKyb()` ne prend aucune raison et ne lève aucun événement ; aucun handler de notification n'existe. Le vendeur découvre `"Rejected"` sur son écran de compte, sans e-mail, sans motif — et re-téléverse la même pièce refusée.

### 7.5 Un produit rejeté ne peut plus être approuvé

Le rejet écrit `Archived`, et `Activate` refuse explicitement un produit archivé. Le couple *Rejeter → corriger → Approuver* — le geste naturel d'un modérateur — renvoie 409. La sortie existe (repasser par `Draft`) mais aucun code ni message ne l'indique. À noter, la contradiction documentaire : `Archive` s'annonce « transition terminale » et `Unarchive` la défait quatre lignes plus loin.

### 7.6 Création partielle non compensée

`CreateProductWithImagesCommand` téléverse les images **avant** toute écriture en base, ce que le commentaire justifie. Mais si `Product.Create` échoue, si `AddMedia` échoue, ou si le `SaveChanges` lève — collision de slug, timeout, panne —, **aucune suppression n'est appelée** sur les objets déjà écrits. Objets orphelins facturés, jamais référencés, jamais nettoyés : aucun ramasse-miettes n'existe.

Accessoirement, ce même commentaire annonce un téléversement « EN UNE SEULE requête batch » ; l'implémentation est une boucle séquentielle.

### 7.7 Course sur le slug et le SKU, sans rattrapage

`SlugExistsAsync` puis `INSERT`, sans verrou ni contrainte rattrapée. Deux créations simultanées du même nom — double-tap sur l'app, rejeu en réseau instable — envoient l'une des deux en 500, **après** que les images ont été téléversées. Le vendeur voit un échec, réessaie, et paie deux fois le stockage.

### 7.8 Renommer un produit devient impossible dès qu'un homonyme existe

La création dédoublonne le slug (`-2`, `-3`…) ; la mise à jour refuse en 409. Le slug est **global** : un vendeur qui renomme son article alors qu'un *autre* vendeur a déjà ce nom voit **toute** sa mise à jour rejetée — description, marque, attributs, tags inclus. Et le message renvoie le slug pris : oracle d'énumération des noms de produits d'autrui.

### 7.9 Le slug d'un produit publié est réécrit à chaque renommage

`Product.Update` recalcule le slug sans condition de statut, alors que `Slug.cs:64-66` affirme : « Une fois publié, on ne le corrige plus sans casser des liens ». Corriger une faute de frappe casse les liens partagés, les favoris et le référencement, sans redirection.

### 7.10 Aucun verrou optimiste sur `Offer`

Grep de `UsePostgresRowVersion|IsConcurrencyToken` dans le module Offers : zéro. Le job d'expiration des remises charge cent offres, boucle, puis sauvegarde. Si le vendeur applique une nouvelle remise entre la lecture et l'écriture, celle-ci est **effacée sans erreur ni trace**, et l'offre repart au prix recalculé au barème courant (§3.3).

### 7.11 Pagination et bornes

Non bornés : `ListSellersQuery` (charge **tous** les vendeurs avec **toutes** leurs pièces KYB, RIB et URL de documents compris, à chaque ouverture de la console), `SellerOfferEndpoints.ListMineAsync`, `SellerInventoryEndpoints.ListMineAsync`, `ListPromotionsQuery`. Le dépôt possède pourtant `SellerBff.Paginate` et un plafond documenté « ABSOLU » — appliqué sur la surface vendeur, pas sur la surface admin.

Non borné également : le **nombre d'images** à la création. Chaque fichier est plafonné à 5 Mo, mais aucune limite sur le compte, et aucune configuration de taille de corps multipart dans tout le dépôt. Une requête de centaines de petites images valides est bufferisée en mémoire puis déclenche autant d'écritures distantes séquentielles.

### 7.12 Identifiant client du service média en dur dans le dépôt

```csharp
/// <summary>Identifiant client (en-tête « x-client-id ») — secret, hors du dépôt.</summary>
public string ClientId { get; set; } = "4f227e9b-…";
```

Le commentaire dit « hors du dépôt » ; la valeur est dans le dépôt, et l'installeur le confirme en assumant de conserver « le ClientId réel ». C'est ce même en-tête qui autorise la suppression de médias.

---

## 8. Ce qui est solide

À protéger en corrigeant le reste.

**La validation d'upload par octets d'en-tête** (`UploadValidation.cs:65-114`) est exemplaire : le type MIME réel est déduit des *magic bytes*, la liste blanche s'applique dessus, la taille est vérifiée avant toute lecture, le message de refus est volontairement identique dans les deux cas, et c'est le type réel — pas celui annoncé — qui est propagé au stockage.

**Le piège `SellerId` / `UserId` est évité partout.** Vérifié ligne à ligne sur les quatre segments : `seller.Id` pour tout ce qui est boutique, `CurrentUserId(principal)` pour tout ce qui est compte, et les conversions explicites sont commentées comme telles. C'est le défaut qui a mordu ailleurs dans ce dépôt ; il ne mord pas ici.

**L'anti-IDOR du BFF vendeur** est homogène : l'identité part toujours du `sub` du jeton, aucun endpoint `/seller/*` n'accepte un identifiant de boutique du client, `NotFoundOrForbidden` ne distingue jamais « inexistant » de « pas à vous », `OwnsLocationAsync` échoue fermé, et la propriété d'un article de stock est reconstituée par chaînage article → localisation → propriétaire.

**Le gel du barème est complet de bout en bout** : offre → résumé → ligne de panier → ligne de commande → répartition. Colonnes `numeric(6,4)` partout, quatre migrations cohérentes, et la reprise de données déduit les taux historiques des montants plutôt que d'imposer le défaut. Seul le chemin « retrait de remise » y échappe.

**L'invalidation de cache de Catalog** (`CatalogDbContext.SaveChangesAsync:76-196`) est la pièce la plus rigoureuse de tout le périmètre : le change tracker comme source de vérité, les enfants remontés au parent par la clé étrangère fantôme, l'éviction **après** le commit, et un vrai bug documenté et corrigé (`is Guid` toujours faux sur un identifiant fortement typé).

**Tous les consumers de Search sont naturellement idempotents** — les huit ont été vérifiés un par un. Un rejeu d'outbox n'indexe pas deux fois, alors qu'il n'existe aucune table d'inbox : l'idempotence est portée par la sémantique des handlers, et elle tient. Aucun `Result.Failure` n'y est avalé.

**Le traitement d'image local** (`RembgImageProcessor`) : plafond mémoire justifié par un calcul explicite, dimensions lues sur l'en-tête avant décodage, orientation EXIF relue sur l'original puisque le PNG de sortie la perd, matrice affine couvrant les huit cas de la norme, liste blanche de modèles pour raison de licence. Et `/media/capabilities` refuse honnêtement de promettre un détourage qui n'aura pas lieu.

**Le repli ASCII du slug sans ICU** : le diagnostic est juste — `InvariantGlobalization` rend `String.Normalize` inopérant — et la table explicite, ligatures comprises, est le bon choix.

**L'anti-énumération à l'inscription vendeur** est réellement uniforme : la commande d'inscription est envoyée dans les deux branches, la réponse ne varie ni en corps ni en statut, le délai de renvoi est constant, et la vérification du code hache **avant** le test de nullité pour ne pas rendre le refus mesurable au chronomètre.

**Les intégrités posées au bon endroit** : « un compte = une boutique » garanti par index unique en base et pas seulement par un test applicatif ; un lieu portant du stock refusé **dans le domaine** ; un produit portant des offres refusé côté BFF en distinguant ses offres de celles des tiers ; la fuite de `SellerSummary` refermée par un `ToPublic()` effectivement appliqué sur les deux surfaces anonymes.

**Le vendeur ne peut pas s'auto-publier.** La mise en vitrine passe obligatoirement par la validation admin. C'est le garde-fou central du parcours — et c'est précisément pour cela que §2.1, qui le contourne par l'accès direct, est classé bloquant.

---

## 9. Ordre de réparation proposé

D'abord ce qui laisse entrer ou sortir quelque chose, ensuite ce qui casse un parcours, enfin ce qui ment.

1. **§2.1 — filtrer le statut du produit sur les lectures anonymes.** Un seul endroit à corriger, `CatalogModuleApi.GetProductAsync`, plus la décision de ce que voient les trois surfaces. C'est la barrière de modération qui ne tient pas.
2. **§7.1 — l'`ExternalId` ne doit plus venir du client.** La clé de stockage doit être produite par le serveur au téléversement et jamais acceptée en entrée.
3. **§1.1 et §1.5 — le droit de vendre.** Un point de passage unique, appelé par toutes les surfaces d'écriture vendeur, plutôt qu'un contrôle recopié.
4. **§3.1 — l'arrondi de l'offre.** `Money.Round` existe désormais ; il suffit de valider la devise et de l'appeler. Corrige aussi §3.5.
5. **§1.2 — `Suspend()` doit émettre**, avec le consommateur Catalog qui dépublie. §1.3 se referme largement avec.
6. **§4.1 — vérifier que le SKU appartient au produit.** Quelques lignes, et cela ferme la vente croisée de stock.
7. **§3.2 — les deux champs oubliés du mapper.** Deux lignes, et la promotion devient visible.
8. **§1.4 — `/api/sellers/*`** : poser le rôle, et traiter le cul-de-sac existant.
9. **§5.1 et §5.6 — l'événement de mise à jour produit, et une commande de reconstruction d'index.** La seconde rattrape tous les défauts d'index, y compris ceux qu'on n'a pas trouvés.
10. **§4.5 et §7.7 — traduire les violations d'unicité en 409.** Un gestionnaire d'exception, et trois 500 disparaissent.
11. **§7.3, §7.4, §7.5** — le KYB vidé après validation, le rejet muet, le produit rejeté irrécupérable.
12. Le reste, en commençant par les modules-décor (§6) : les documenter comme non branchés coûte une heure et fait gagner des jours au prochain lecteur.

---

*Audit mené sans compilateur ni base de données. Les constats sont établis par lecture ; aucun n'a été reproduit à l'exécution. Les neuf plus graves ont été revérifiés à la main après le rapport initial.*
