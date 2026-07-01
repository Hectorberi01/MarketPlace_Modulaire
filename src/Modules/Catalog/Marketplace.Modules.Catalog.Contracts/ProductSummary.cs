namespace Marketplace.Modules.Catalog.Contracts;

/// <summary>
/// Vue publique d'un produit, exposée aux autres modules via l'API in-process.
/// DTO stable : ne reflète pas la structure interne de l'agrégat.
/// </summary>
public sealed record ProductSummary(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    string Slug,
    string Status,
    string? Gtin,
    string? Ean,
    Guid? ProductGroupId,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ProductVariantSummary> Variants,
    IReadOnlyList<ProductMediaSummary> Media);

public sealed record ProductVariantSummary(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    string? Barcode,
    int WeightGrams);

public sealed record ProductMediaSummary(
    Guid Id,
    string Url,
    string Type,
    bool IsPrimary,
    int Position,
    string AltText);
