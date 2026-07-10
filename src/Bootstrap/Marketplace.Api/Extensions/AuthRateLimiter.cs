using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Politique de rate limiting partagée pour les endpoints d'authentification
/// anonymes (login / refresh / mot de passe oublié / reset). Fenêtre fixe par
/// adresse IP : freine le brute-force et l'énumération de comptes.
/// </summary>
public static class AuthRateLimiter
{
    public const string PolicyName = "auth";

    private const int PermitPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAuthRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitPerWindow,
                        Window = Window,
                        QueueLimit = 0
                    }));
        });
        return services;
    }
}
