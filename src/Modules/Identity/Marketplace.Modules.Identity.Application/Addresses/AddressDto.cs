namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>Vue d'une adresse du carnet utilisateur.</summary>
public sealed record AddressDto(
    Guid Id,
    string Label,
    string Recipient,
    string Line1,
    string? Line2,
    string City,
    string Country,
    string Phone,
    bool IsDefault);
