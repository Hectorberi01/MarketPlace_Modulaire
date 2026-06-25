namespace Marketplace.Modules.Cart.Domain.Carts;

/// <summary>Identité forte d'un panier.</summary>
public readonly record struct CartId(Guid Value)
{
    public static CartId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut du panier.</summary>
public enum CartStatus
{
    Active = 0,
    CheckedOut = 1,
    Abandoned = 2
}
