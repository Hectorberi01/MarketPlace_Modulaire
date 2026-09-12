using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products.Events;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Source de vérité d'un produit. Modèle B : le produit appartient à un seul
/// vendeur. Ne gère ni prix transactionnel ni stock ; il émet les events
/// consommés par Search, Inventory, etc. (cf. dossier, module Catalog).
///
/// Agrégat racine : il possède ses variantes (ProductVariant) et ses médias
/// (ProductMedia), qui ne sont modifiés qu'à travers lui.
/// </summary>
public sealed class Product : AggregateRoot<ProductId>
{
    private readonly List<ProductVariant> _variants = new();
    private readonly List<ProductMedia> _media = new();

    // ctor EF.
    private Product()
    {
    }

    private Product(
        ProductId id,
        Guid sellerId,
        Guid categoryId,
        Guid? brandId,
        string name,
        Slug slug,
        string description,
        string? gtin,
        string? ean,
        Guid? productGroupId,
        Dictionary<string, string> attributes,
        List<string> tags)
        : base(id)
    {
        SellerId = sellerId;
        CategoryId = categoryId;
        BrandId = brandId;
        Name = name;
        Slug = slug;
        Description = description;
        Gtin = gtin;
        Ean = ean;
        ProductGroupId = productGroupId;
        Attributes = attributes;
        Tags = tags;
        Status = ProductStatus.Draft;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new ProductCreatedDomainEvent(id.Value, sellerId, categoryId, name, slug.Value));
    }

    public Guid SellerId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid? BrandId { get; private set; }
    public string Name { get; private set; } = default!;
    public Slug Slug { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string? Gtin { get; private set; }
    public string? Ean { get; private set; }

    /// <summary>Clé de regroupement souple des fiches identiques entre vendeurs.</summary>
    public Guid? ProductGroupId { get; private set; }

    /// <summary>Attributs dynamiques selon la catégorie (mappés en jsonb).</summary>
    public Dictionary<string, string> Attributes { get; private set; } = new();

    /// <summary>Mots-clés (index GIN), mappés en text[].</summary>
    public List<string> Tags { get; private set; } = new();

    public ProductStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    public IReadOnlyCollection<ProductMedia> Media => _media.AsReadOnly();

    /// <summary>
    /// Crée un produit (Draft) en validant les invariants. Renvoie un Result :
    /// l'échec attendu n'est pas une exception.
    /// </summary>
    public static Result<Product> Create(
        Guid sellerId,
        Guid categoryId,
        string name,
        string description,
        Guid? brandId = null,
        string? gtin = null,
        string? ean = null,
        Guid? productGroupId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IEnumerable<string>? tags = null,
        // Slug déjà résolu (dédupliqué) par l'Application. Null → dérivé du nom.
        // Passer un slug garantit que l'événement de création porte le slug DÉFINITIF,
        // ce qui serait faux si on le corrigeait après construction.
        Slug? slug = null)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("catalog.product.seller_required", "Un produit doit appartenir à un vendeur.");
        }

        if (categoryId == Guid.Empty)
        {
            return Error.Validation("catalog.product.category_required", "Un produit doit être rattaché à une catégorie.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.product.name_required", "Le nom du produit est obligatoire.");
        }

        Slug productSlug;
        if (slug is not null)
        {
            productSlug = slug;
        }
        else
        {
            var slugResult = Slug.Create(name);
            if (slugResult.IsFailure)
            {
                return Result.Failure<Product>(slugResult.Error);
            }

            productSlug = slugResult.Value;
        }

        var product = new Product(
            ProductId.New(),
            sellerId,
            categoryId,
            brandId == Guid.Empty ? null : brandId,
            name.Trim(),
            productSlug,
            description?.Trim() ?? string.Empty,
            Clean(gtin),
            Clean(ean),
            productGroupId,
            attributes is null ? new Dictionary<string, string>() : new Dictionary<string, string>(attributes),
            // TAGS RÉSERVÉS RETIRÉS. Un produit ne peut pas naître « mis en avant » :
            // la mise en avant est une décision éditoriale, elle passe par la route
            // d'administration dédiée. Voir `ProductTags`.
            ProductTags.FromSeller(tags));

        return product;
    }

    /// <summary>Ajoute une variante (SKU unique au sein du produit).</summary>
    public Result<ProductVariant> AddVariant(
        string sku,
        IReadOnlyDictionary<string, string>? variantAttributes,
        string? barcode,
        int weightGrams,
        int? lengthMm = null,
        int? widthMm = null,
        int? heightMm = null)
    {
        if (weightGrams < 0)
        {
            return Error.Validation("catalog.variant.weight_negative", "Le poids ne peut pas être négatif.");
        }

        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<ProductVariant>(skuResult.Error);
        }

        if (_variants.Any(v => v.Sku == skuResult.Value))
        {
            return Error.Conflict("catalog.variant.sku_duplicate", $"Le SKU « {skuResult.Value.Value} » existe déjà sur ce produit.");
        }

        Dimensions? dimensions = null;
        if (lengthMm.HasValue || widthMm.HasValue || heightMm.HasValue)
        {
            var dimensionsResult = Dimensions.Create(lengthMm ?? 0, widthMm ?? 0, heightMm ?? 0);
            if (dimensionsResult.IsFailure)
            {
                return Result.Failure<ProductVariant>(dimensionsResult.Error);
            }

            dimensions = dimensionsResult.Value;
        }

        var attributes = variantAttributes is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(variantAttributes);

        var variant = new ProductVariant(Guid.NewGuid(), skuResult.Value, attributes, Clean(barcode), weightGrams, dimensions);
        _variants.Add(variant);

        return variant;
    }

    /// <summary>Met à jour une variante existante (SKU, attributs, code-barres, poids).</summary>
    public Result UpdateVariant(
        Guid variantId,
        string sku,
        IReadOnlyDictionary<string, string>? variantAttributes,
        string? barcode,
        int weightGrams)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(Error.NotFound("catalog.variant.not_found", $"Variante {variantId} introuvable sur ce produit."));
        }

        if (weightGrams < 0)
        {
            return Result.Failure(Error.Validation("catalog.variant.weight_negative", "Le poids ne peut pas être négatif."));
        }

        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure(skuResult.Error);
        }

        if (_variants.Any(v => v.Id != variantId && v.Sku == skuResult.Value))
        {
            return Result.Failure(Error.Conflict("catalog.variant.sku_duplicate", $"Le SKU « {skuResult.Value.Value} » existe déjà sur ce produit."));
        }

        var attributes = variantAttributes is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(variantAttributes);

        variant.Update(skuResult.Value, attributes, Clean(barcode), weightGrams);
        return Result.Success();
    }

    /// <summary>Retire une variante du produit.</summary>
    public Result RemoveVariant(Guid variantId)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(Error.NotFound("catalog.variant.not_found", $"Variante {variantId} introuvable sur ce produit."));
        }

        _variants.Remove(variant);
        return Result.Success();
    }

    /// <summary>Ajoute un média (la première image devient principale).</summary>
    public Result<ProductMedia> AddMedia(
        string url,
        ProductMediaType type,
        string? altText,
        bool isPrimary,
        string? externalId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Error.Validation("catalog.media.url_required", "L'URL du média est obligatoire.");
        }

        var makePrimary = isPrimary || _media.Count == 0;
        if (makePrimary)
        {
            foreach (var existing in _media)
            {
                existing.UnsetPrimary();
            }
        }

        var media = new ProductMedia(
            Guid.NewGuid(),
            url.Trim(),
            type,
            altText?.Trim() ?? string.Empty,
            _media.Count,
            makePrimary,
            Clean(externalId));

        _media.Add(media);

        return media;
    }

    /// <summary>
    /// Retire un média du produit et renvoie l'entité retirée (pour permettre la
    /// suppression côté service média via son ExternalId). Recompacte les positions
    /// et promeut un nouveau média principal si nécessaire.
    /// </summary>
    public Result<ProductMedia> RemoveMedia(Guid mediaId)
    {
        var media = _media.FirstOrDefault(m => m.Id == mediaId);
        if (media is null)
        {
            return Error.NotFound("catalog.media.not_found", $"Média {mediaId} introuvable sur ce produit.");
        }

        _media.Remove(media);

        // Recompacte les positions (0..n-1).
        var ordered = _media.OrderBy(m => m.Position).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i);
        }

        // Si on a retiré l'image principale, le premier média restant la devient.
        if (media.IsPrimary && ordered.Count > 0)
        {
            ordered[0].MakePrimary();
        }

        return media;
    }

    /// <summary>Définit le média principal (les autres perdent le statut principal).</summary>
    public Result SetPrimaryMedia(Guid mediaId)
    {
        var target = _media.FirstOrDefault(m => m.Id == mediaId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("catalog.media.not_found", $"Média {mediaId} introuvable sur ce produit."));
        }

        foreach (var m in _media)
        {
            m.UnsetPrimary();
        }
        target.MakePrimary();
        return Result.Success();
    }

    /// <summary>
    /// Réordonne les médias selon la liste d'identifiants fournie (positions 0..n-1).
    /// Les ids inconnus sont ignorés ; les médias non listés sont placés à la fin
    /// dans leur ordre courant. Le premier média devient l'image principale.
    /// </summary>
    public Result ReorderMedia(IReadOnlyList<Guid> orderedMediaIds)
    {
        if (_media.Count == 0)
        {
            return Result.Success();
        }

        var ordered = orderedMediaIds
            .Select(id => _media.FirstOrDefault(m => m.Id == id))
            .Where(m => m is not null)
            .Cast<ProductMedia>()
            .ToList();

        // Ajoute les médias non mentionnés à la fin (ordre courant).
        foreach (var m in _media.OrderBy(m => m.Position))
        {
            if (!ordered.Contains(m))
            {
                ordered.Add(m);
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i);
            ordered[i].UnsetPrimary();
        }
        ordered[0].MakePrimary();
        return Result.Success();
    }

    public void AssignBrand(Guid? brandId) => BrandId = brandId == Guid.Empty ? null : brandId;

    /// <summary>
    /// Remplace la liste des tags (nettoyés, sans doublons ni vides). Sert la
    /// curation éditoriale : marquer un produit « featured » ne demande qu'un tag,
    /// sans colonne dédiée. Idempotent.
    ///
    /// <para>
    /// SEULE MÉTHODE QUI ACCEPTE LES TAGS RÉSERVÉS, et c'est sa raison d'être.
    /// `Create` les retire et `Update` reconduit ceux déjà posés : ni l'un ni l'autre
    /// ne permet d'en ajouter. Celle-ci n'est atteignable que par la route
    /// d'administration (`SetProductTagsCommand`), qui doit pouvoir mettre en avant
    /// ET retirer la mise en avant.
    /// </para>
    /// </summary>
    public void SetTags(IReadOnlyList<string>? tags) => Tags = ProductTags.Clean(tags);

    /// <summary>Publie le produit (Draft -> Active).</summary>
    public Result Activate()
    {
        if (Status == ProductStatus.Archived)
        {
            // LE MESSAGE DIT LA SORTIE, PARCE QU'ELLE EXISTE.
            //
            // Le refus était exact mais muet : ni le code ni le texte n'indiquaient
            // que `Unarchive` ramène le produit en brouillon et rouvre la
            // publication. Un modérateur qui enchaînait « rejeter, corriger,
            // approuver » — le geste naturel — se prenait un 409 sans issue visible.
            // Le rejet, lui, ne passe plus par l'archivage (voir la modération).
            return Result.Failure(Error.Conflict(
                "catalog.product.archived",
                "Un produit archivé ne peut pas être publié directement : repassez-le d'abord en brouillon."));
        }

        // Idempotent mais émet toujours l'event : re-valider rafraîchit la
        // visibilité et la vignette en vitrine (utile pour réindexer).
        Status = ProductStatus.Active;
        Raise(new ProductStatusChangedDomainEvent(Id.Value, Status.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Archive le produit : il quitte la vente et n'est plus publiable en l'état.
    ///
    /// PAS « TERMINALE », contrairement à ce que disait cette ligne quatre lignes
    /// au-dessus d'<see cref="Unarchive"/>, qui la défait. Le mot juste est
    /// RETRAIT : le produit sort du catalogue actif, et il faut un geste explicite —
    /// le repasser en brouillon — pour le remettre en jeu. C'est ce détour qui est
    /// voulu, pas l'irréversibilité.
    /// </summary>
    public Result Archive()
    {
        Status = ProductStatus.Archived;
        Raise(new ProductStatusChangedDomainEvent(Id.Value, Status.ToString()));
        return Result.Success();
    }

    /// <summary>Remet un produit archivé en brouillon (réédition).</summary>
    public Result Unarchive()
    {
        Status = ProductStatus.Draft;
        Raise(new ProductStatusChangedDomainEvent(Id.Value, Status.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Dépublie un produit actif (Active -> Draft) : il est retiré de la vente mais
    /// reste éditable et republiable d'un geste. Utilisé quand le vendeur ferme son
    /// compte (retrait réversible, contrairement à l'archivage terminal). Sans effet
    /// sur un produit déjà en brouillon ou archivé.
    /// </summary>
    public Result Unpublish()
    {
        if (Status != ProductStatus.Active)
        {
            return Result.Success();
        }

        Status = ProductStatus.Draft;
        Raise(new ProductStatusChangedDomainEvent(Id.Value, Status.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Met à jour les champs descriptifs du produit. Le slug est recalculé à
    /// partir du nouveau nom ; l'unicité éventuelle est vérifiée par l'Application.
    /// Ne touche ni aux variantes ni aux médias (gérés par leurs propres opérations).
    /// </summary>
    public Result Update(
        string name,
        string description,
        Guid? brandId,
        string? gtin,
        string? ean,
        Guid? productGroupId,
        IReadOnlyDictionary<string, string>? attributes,
        IEnumerable<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("catalog.product.name_required", "Le nom du produit est obligatoire."));
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        Name = name.Trim();
        Slug = slugResult.Value;
        Description = description?.Trim() ?? string.Empty;
        BrandId = brandId == Guid.Empty ? null : brandId;
        Gtin = Clean(gtin);
        Ean = Clean(ean);
        ProductGroupId = productGroupId;
        Attributes = attributes is null ? new Dictionary<string, string>() : new Dictionary<string, string>(attributes);
        // ─────────────────────────────────────────────────────────────────────────
        // LE VENDEUR REMPLACE SES TAGS, PAS CEUX DE L'ADMINISTRATION.
        //
        // `Update` remplace la liste ENTIÈRE. Filtrer simplement les tags réservés de
        // l'entrée aurait effacé la mise en avant à la première correction de nom faite
        // par le vendeur — l'administration aurait perdu sa décision sans que personne
        // ne s'en aperçoive. Les réservés sont donc repris de l'état courant.
        // ─────────────────────────────────────────────────────────────────────────
        Tags = ProductTags.MergeFromSeller(tags, Tags);

        // L'ÉVÉNEMENT QUI MANQUAIT. Sans lui l'index de recherche gardait le nom et
        // le slug du jour de la création : le produit renommé restait trouvable sous
        // l'ancien nom, et introuvable sous le nouveau.
        Raise(new ProductUpdatedDomainEvent(Id.Value, SellerId, CategoryId, Name, Slug.Value));

        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
