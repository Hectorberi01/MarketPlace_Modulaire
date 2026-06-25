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
        IEnumerable<string>? tags = null)
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

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Product>(slugResult.Error);
        }

        var product = new Product(
            ProductId.New(),
            sellerId,
            categoryId,
            brandId == Guid.Empty ? null : brandId,
            name.Trim(),
            slugResult.Value,
            description?.Trim() ?? string.Empty,
            Clean(gtin),
            Clean(ean),
            productGroupId,
            attributes is null ? new Dictionary<string, string>() : new Dictionary<string, string>(attributes),
            tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList() ?? new List<string>());

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

    public void AssignBrand(Guid? brandId) => BrandId = brandId == Guid.Empty ? null : brandId;

    /// <summary>Publie le produit (Draft -> Active).</summary>
    public Result Activate()
    {
        if (Status == ProductStatus.Active)
        {
            return Result.Success();
        }

        if (Status == ProductStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.product.archived", "Un produit archivé ne peut pas être réactivé."));
        }

        Status = ProductStatus.Active;
        return Result.Success();
    }

    /// <summary>Archive le produit (le retire de la vente). Transition terminale.</summary>
    public Result Archive()
    {
        Status = ProductStatus.Archived;
        return Result.Success();
    }

    /// <summary>Remet un produit archivé en brouillon (réédition).</summary>
    public Result Unarchive()
    {
        Status = ProductStatus.Draft;
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
        Tags = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList() ?? new List<string>();

        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
