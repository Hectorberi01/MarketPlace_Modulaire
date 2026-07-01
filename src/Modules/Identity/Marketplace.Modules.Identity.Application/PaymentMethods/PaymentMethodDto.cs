namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>
/// Projection client d'un moyen de paiement enregistré. <see cref="Display"/>
/// est l'affichage masqué prêt à l'emploi (numéro Mobile Money ou « •••• 4242 »).
/// </summary>
public sealed record PaymentMethodDto(
    Guid Id,
    string Type,
    string Label,
    string Provider,
    string Display,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? HolderName,
    bool IsDefault);
