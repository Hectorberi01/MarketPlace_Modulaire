namespace Marketplace.Modules.Catalog.Domain.Brands;

/// <summary>Identité forte d'une marque.</summary>
public readonly record struct BrandId(Guid Value)
{
    public static BrandId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
