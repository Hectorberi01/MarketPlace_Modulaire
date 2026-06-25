using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>Jeton d'accès JWT émis et sa date d'expiration (UTC).</summary>
public sealed record AccessToken(string Token, DateTime ExpiresOnUtc);

/// <summary>
/// Génère un JWT signé portant l'identité, les rôles et les permissions de
/// l'utilisateur (implémenté en Infrastructure).
/// </summary>
public interface IJwtTokenGenerator
{
    AccessToken Generate(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
}
