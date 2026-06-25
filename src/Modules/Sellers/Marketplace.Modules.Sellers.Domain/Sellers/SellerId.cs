namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>Identité forte d'un vendeur.</summary>
public readonly record struct SellerId(Guid Value)
{
    public static SellerId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
