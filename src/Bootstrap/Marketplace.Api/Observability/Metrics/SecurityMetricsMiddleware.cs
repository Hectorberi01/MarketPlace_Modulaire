using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Marketplace.Api.Observability.Metrics;

/// <summary>
/// Middleware qui compte les réponses de sécurité 401 / 403 / 429 dans
/// <see cref="SecurityMetrics"/>. Il utilise le <b>motif de route</b>
/// (ex. <c>/admin/users/{id}</c>) comme label — jamais l'URL complète — pour
/// garder une cardinalité faible.
///
/// À placer APRÈS l'authentification, l'autorisation et le rate limiter dans le
/// pipeline (sinon le code de statut n'est pas encore fixé).
/// </summary>
public sealed class SecurityMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityMetrics _metrics;

    public SecurityMetricsMiddleware(RequestDelegate next, SecurityMetrics metrics)
    {
        _next = next;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        switch (context.Response.StatusCode)
        {
            case StatusCodes.Status401Unauthorized:
                _metrics.Unauthorized(RouteOf(context));
                break;
            case StatusCodes.Status403Forbidden:
                _metrics.Forbidden(RouteOf(context));
                break;
            case StatusCodes.Status429TooManyRequests:
                _metrics.RateLimited(RouteOf(context));
                break;
        }
    }

    /// <summary>Motif de route (templated) ou « unknown » — jamais l'URL brute.</summary>
    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
}
