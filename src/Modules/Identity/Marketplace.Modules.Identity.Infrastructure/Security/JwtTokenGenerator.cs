using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Émet un JWT signé (HMAC-SHA256) portant l'identité, les rôles et les
/// permissions. Le « security_stamp » permet d'invalider les tokens après un
/// changement sensible (mot de passe, MFA).
/// </summary>
internal sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    public const string PermissionClaimType = "permission";
    public const string SecurityStampClaimType = "security_stamp";

    private readonly JwtOptions _options;

    public JwtTokenGenerator(JwtOptions options)
        => _options = options;

    public AccessToken Generate(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var expiresOnUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SecurityStampClaimType, user.SecurityStamp.ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(PermissionClaimType, permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresOnUtc,
            signingCredentials: credentials);

        var serialized = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(serialized, expiresOnUtc);
    }
}
