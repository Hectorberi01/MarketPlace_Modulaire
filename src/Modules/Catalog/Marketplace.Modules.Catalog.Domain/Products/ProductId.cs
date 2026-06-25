namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Identité forte du produit (strongly-typed id). Évite de confondre un
/// ProductId avec un SellerId ou un CategoryId à la compilation.
/// </summary>
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
