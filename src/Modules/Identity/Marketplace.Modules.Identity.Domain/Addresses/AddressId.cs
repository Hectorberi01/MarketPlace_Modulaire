namespace Marketplace.Modules.Identity.Domain.Addresses;

/// <summary>Identité forte d'une adresse du carnet utilisateur.</summary>
public readonly record struct AddressId(Guid Value)
{
    public static AddressId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
