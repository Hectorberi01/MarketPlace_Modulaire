using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Identity.Domain.Addresses;

/// <summary>
/// Adresse de livraison du carnet d'un utilisateur. Agrégat racine simple,
/// rattaché à un compte par <see cref="UserId"/> (référence inter-agrégat, pas de FK).
/// </summary>
public sealed class Address : AggregateRoot<AddressId>
{
    private Address()
    {
    }

    private Address(
        AddressId id,
        Guid userId,
        string label,
        string recipient,
        string line1,
        string? line2,
        string city,
        string country,
        string phone,
        bool isDefault)
        : base(id)
    {
        UserId = userId;
        Label = label;
        Recipient = recipient;
        Line1 = line1;
        Line2 = line2;
        City = city;
        Country = country;
        Phone = phone;
        IsDefault = isDefault;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Label { get; private set; } = default!;
    public string Recipient { get; private set; } = default!;
    public string Line1 { get; private set; } = default!;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = default!;
    public string Country { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public bool IsDefault { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static Result<Address> Create(
        Guid userId, string? label, string? recipient, string line1, string? line2,
        string city, string? country, string? phone, bool isDefault)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<Address>(Error.Validation("identity.address.user_required", "Utilisateur requis."));
        }

        if (string.IsNullOrWhiteSpace(line1))
        {
            return Result.Failure<Address>(Error.Validation("identity.address.line_required", "L'adresse est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return Result.Failure<Address>(Error.Validation("identity.address.city_required", "La ville est obligatoire."));
        }

        return Result.Success(new Address(
            AddressId.New(),
            userId,
            (label ?? "Adresse").Trim(),
            (recipient ?? string.Empty).Trim(),
            line1.Trim(),
            string.IsNullOrWhiteSpace(line2) ? null : line2!.Trim(),
            city.Trim(),
            (country ?? string.Empty).Trim(),
            (phone ?? string.Empty).Trim(),
            isDefault));
    }

    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;
}
