using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Identity.Contracts.IntegrationEvents;

/// <summary>Un compte a été créé. Consommé par Notifications (bienvenue), Analytics…</summary>
public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
}

/// <summary>
/// Une vérification d'e-mail est demandée. Porte le jeton EN CLAIR (à usage
/// unique, courte durée) que Notifications insère dans le lien envoyé.
/// </summary>
public sealed record EmailVerificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string VerificationToken { get; init; }
}

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
public sealed record UserEmailConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
}
