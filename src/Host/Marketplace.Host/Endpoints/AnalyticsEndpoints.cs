using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Analytics.Application.Metrics;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Analytics : rapports agrégés (GMV, commandes,
/// articles) par période, à l'échelle plateforme (Admin) ou vendeur. Read model
/// alimenté par les événements de commande.
/// </summary>
public static class AnalyticsEndpoints
{
    /// <summary>Enregistre les routes de reporting.</summary>
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapAdminGroup("/api/analytics").WithTags("Analytics");

        analytics.MapGet("/platform", PlatformAsync).WithName("GetPlatformReport").RequireAuthorization(p => p.RequireRole("Admin"));
        analytics.MapGet("/sellers/{sellerId:guid}", SellerAsync).WithName("GetSellerReport").RequireAuthorization();

        return app;
    }

    /// <summary>Rapport plateforme sur une plage de périodes (clés yyyy-MM-dd ou yyyy-MM).</summary>
    private static async Task<IResult> PlatformAsync(string from, string to, string? granularity, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetReportQuery("Platform", null, granularity ?? "Day", from, to), ct)).Match(r => Results.Ok(r));

    /// <summary>Rapport d'un vendeur sur une plage de périodes.</summary>
    private static async Task<IResult> SellerAsync(Guid sellerId, string from, string to, string? granularity, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetReportQuery("Seller", sellerId, granularity ?? "Day", from, to), ct)).Match(r => Results.Ok(r));
}
