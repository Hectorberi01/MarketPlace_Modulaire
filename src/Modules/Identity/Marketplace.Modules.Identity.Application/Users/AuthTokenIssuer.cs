using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Models;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users;

/// <summary>
/// Émet une paire de jetons pour un utilisateur : résout ses rôles et
/// permissions (pour les claims JWT), génère l'access token et un refresh token
/// (dont seul le hash est stocké sur l'agrégat). Ne persiste pas — l'appelant
/// committe via l'Unit of Work.
/// </summary>
internal sealed class AuthTokenIssuer
{
    private readonly IRoleRepository _roleRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;

    public AuthTokenIssuer(
        IRoleRepository roleRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings)
    {
        _roleRepository = roleRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
    }

    public async Task<AuthTokens> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var roleIds = user.RoleIds.Select(id => new RoleId(id)).ToList();
        var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);

        var roleNames = roles.Select(r => r.Name).ToList();
        var permissions = roles.SelectMany(r => r.Permissions).Distinct().ToList();

        var accessToken = _jwtTokenGenerator.Generate(user, roleNames, permissions);

        var (rawRefresh, refreshHash) = _tokenGenerator.Generate();
        var refreshExpiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.RefreshTokenLifetime);
        user.IssueRefreshToken(refreshHash, refreshExpiresOnUtc);

        return new AuthTokens(accessToken.Token, accessToken.ExpiresOnUtc, rawRefresh, refreshExpiresOnUtc);
    }
}
