using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Identity.Domain.Users.Events;

/// <summary>Un compte vient d'être créé (en attente de vérification).</summary>
public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string FirstName) : DomainEvent;

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
public sealed record UserEmailConfirmedDomainEvent(Guid UserId, string Email) : DomainEvent;

/// <summary>Le mot de passe d'un compte a changé (sessions invalidées).</summary>
public sealed record UserPasswordChangedDomainEvent(Guid UserId) : DomainEvent;

/// <summary>Un rôle a été assigné à un compte.</summary>
public sealed record UserRoleAssignedDomainEvent(Guid UserId, Guid RoleId) : DomainEvent;
