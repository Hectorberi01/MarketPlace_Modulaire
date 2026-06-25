using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Catalog.Domain.Products;

public enum ProductMediaType
{
    Image = 0,
    Video = 1
}

/// <summary>
/// Média rattaché à un produit. Le binaire est en object storage ; si l'image
/// est gérée par un service tiers, on conserve sa référence externe (cf.
/// dossier, ProductMedia). Entité enfant de l'agrégat Product : modifiée
/// uniquement via le root.
/// </summary>
public sealed class ProductMedia : Entity<Guid>
{
    private ProductMedia()
    {
    }

    internal ProductMedia(
        Guid id,
        string url,
        ProductMediaType type,
        string altText,
        int position,
        bool isPrimary,
        string? externalId)
        : base(id)
    {
        Url = url;
        Type = type;
        AltText = altText;
        Position = position;
        IsPrimary = isPrimary;
        ExternalId = externalId;
    }

    public string Url { get; private set; } = default!;
    public ProductMediaType Type { get; private set; }
    public string AltText { get; private set; } = default!;
    public int Position { get; private set; }
    public bool IsPrimary { get; private set; }
    public string? ExternalId { get; private set; }

    internal void UnsetPrimary() => IsPrimary = false;
}
