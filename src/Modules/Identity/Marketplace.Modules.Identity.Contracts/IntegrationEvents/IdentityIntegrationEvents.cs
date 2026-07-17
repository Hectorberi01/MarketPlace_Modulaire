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

/// <summary>
/// Une réinitialisation de mot de passe est demandée. Porte le jeton EN CLAIR (usage
/// unique, 1 h) que Notifications insère dans le lien de l'e-mail.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CET ÉVÉNEMENT N'EXISTAIT PAS — ET C'EST POUR ÇA QUE LE JETON SORTAIT EN HTTP.
///
/// Faute de canal, `RequestPasswordResetCommand` RENVOYAIT le jeton en clair, et
/// l'endpoint anonyme `/mobile/auth/password/forgot` le recopiait dans sa réponse.
/// N'importe qui pouvait prendre le contrôle de n'importe quel compte, admin compris.
///
/// Le jeton a désormais un chemin légitime : il traverse l'outbox jusqu'à Notifications,
/// qui l'envoie par e-mail à son destinataire — et à personne d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// ⚠️ CET ÉVÉNEMENT PORTE UN SECRET. Il transite par la table d'outbox, donc le jeton en
/// clair est écrit en base le temps de la livraison. C'est acceptable — il expire en 1 h,
/// il est à usage unique, et il ne vaut rien sans l'accès à la boîte mail — mais cela
/// signifie que <b>ce record ne doit JAMAIS être journalisé</b>, et qu'aucun handler ne
/// doit l'écrire ailleurs. Ne pas ajouter de `ToString()`, ne pas le passer à un logger.
/// </summary>
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string ResetToken { get; init; }
}
