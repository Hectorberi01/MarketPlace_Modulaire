namespace Marketplace.Modules.Catalog.Domain.Categories;

/// <summary>Identité forte d'une catégorie.</summary>
public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
