using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Déclinaison d'un produit (couleur, taille). Le prix et le stock vivent
/// ailleurs, référencés par le SKU (cf. dossier, ProductVariant). Entité enfant
/// de l'agrégat Product.
/// </summary>
public sealed class ProductVariant : Entity<Guid>
{
    private ProductVariant()
    {
    }

    internal ProductVariant(
        Guid id,
        Sku sku,
        Dictionary<string, string> variantAttributes,
        string? barcode,
        int weightGrams,
        Dimensions? dimensions)
        : base(id)
    {
        Sku = sku;
        VariantAttributes = variantAttributes;
        Barcode = barcode;
        WeightGrams = weightGrams;
        Dimensions = dimensions;
    }

    public Sku Sku { get; private set; } = default!;

    /// <summary>Attributs dynamiques (couleur, taille…), mappés en jsonb.</summary>
    public Dictionary<string, string> VariantAttributes { get; private set; } = new();

    public string? Barcode { get; private set; }
    public int WeightGrams { get; private set; }
    public Dimensions? Dimensions { get; private set; }
}
