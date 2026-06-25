namespace Marketplace.Modules.Offers.Domain.Offers;

/// <summary>Identité forte d'une offre.</summary>
public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
