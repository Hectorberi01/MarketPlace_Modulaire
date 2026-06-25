namespace Marketplace.Modules.Identity.Contracts;

/// <summary>Vue publique d'un compte, exposée aux autres modules et au front.</summary>
public sealed record UserSummary(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Status,
    bool EmailVerified,
    bool MfaEnabled,
    IReadOnlyList<Guid> RoleIds);
