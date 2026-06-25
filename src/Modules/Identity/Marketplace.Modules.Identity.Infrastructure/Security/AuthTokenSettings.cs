using Marketplace.Modules.Identity.Application.Abstractions;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>Expose les durées de vie des jetons aux handlers (depuis JwtOptions).</summary>
internal sealed class AuthTokenSettings : IAuthTokenSettings
{
    private readonly JwtOptions _options;

    public AuthTokenSettings(JwtOptions options)
        => _options = options;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public TimeSpan EmailVerificationLifetime => TimeSpan.FromHours(_options.EmailVerificationHours);
}
