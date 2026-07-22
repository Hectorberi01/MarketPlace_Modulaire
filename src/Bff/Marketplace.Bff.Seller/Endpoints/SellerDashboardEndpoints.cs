using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — tableau de bord. Agrège en un appel les indicateurs clés de la
/// boutique (commandes, chiffre d'affaires sur 30 j, avis), pour l'écran d'accueil.
/// </summary>
public static class SellerDashboardEndpoints
{
    public static IEndpointRouteBuilder MapSellerDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // Route isolée (pas de groupe) : le rôle est exigé ici directement, sinon
        // elle passait à travers le verrouillage posé sur les 17 autres groupes.
        app.MapGet("/seller/dashboard", DashboardAsync)
            .WithTags("Vendeur · Tableau de bord")
            .RequireAuthorization(p => p.RequireRole(SellerBff.SellerRole));

        // Statistiques avancées : tendance des ventes (série journalière ou mensuelle).
        app.MapGet("/seller/analytics", AnalyticsAsync)
            .WithTags("Vendeur · Tableau de bord")
            .RequireAuthorization(p => p.RequireRole(SellerBff.SellerRole));

        return app;
    }

    /// <summary>
    /// Tendance des ventes du vendeur sur une plage : série de périodes (commandes,
    /// articles vendus, GMV) + totaux. Réutilise le rapport agrégé du module Analytics.
    /// </summary>
    private static async Task<IResult> AnalyticsAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, string? granularity, int? days, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var now = DateTime.UtcNow;
        var isMonth = string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase);

        string fromKey, toKey, gran;
        if (isMonth)
        {
            var months = days is > 0 and <= 24 ? days.Value : 12;
            fromKey = now.AddMonths(-(months - 1)).ToString("yyyy-MM");
            toKey = now.ToString("yyyy-MM");
            gran = "Month";
        }
        else
        {
            var d = days is > 0 and <= 180 ? days.Value : 30;
            fromKey = now.AddDays(-(d - 1)).ToString("yyyy-MM-dd");
            toKey = now.ToString("yyyy-MM-dd");
            gran = "Day";
        }

        var result = await sender.Send(new GetReportQuery("Seller", seller.Id, gran, fromKey, toKey), ct);
        return result.Match(r => Results.Ok(r));
    }

    private static async Task<IResult> DashboardAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var now = DateTime.UtcNow;

        // Commandes du vendeur (toutes + à traiter).
        var ordersResult = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
        var orders = ordersResult.IsSuccess ? ordersResult.Value : Array.Empty<Marketplace.Modules.Ordering.Contracts.OrderSummary>();
        var ordersTotal = orders.Count;
        var ordersToProcess = orders.Count(o => o.Status is not ("Delivered" or "Cancelled"));

        // Répartition par statut : la liste est déjà chargée, l'agrégation est
        // gratuite. Alimente le donut « commandes par statut » côté app.
        var ordersByStatus = orders
            .GroupBy(o => o.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // Relevé financier sur 30 jours (brut, commissions, net).
        decimal grossSales = 0, netPayout = 0;
        string currency = "XOF";
        var statement = await sender.Send(new GetSellerStatementQuery(seller.Id, now.AddDays(-30), now), ct);
        if (statement.IsSuccess)
        {
            grossSales = statement.Value.GrossSales;
            netPayout = statement.Value.NetPayout;
            currency = statement.Value.Currency;
        }

        // Avis (nombre + note moyenne).
        var reviewsResult = await sender.Send(new ListReviewsBySellerQuery(seller.Id), ct);
        var reviews = reviewsResult.IsSuccess ? reviewsResult.Value : Array.Empty<Marketplace.Modules.Reviews.Contracts.ReviewSummary>();
        var reviewsCount = reviews.Count;
        var averageRating = reviewsCount > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0d;

        return Results.Ok(new SellerDashboard(
            ordersTotal, ordersToProcess, grossSales, netPayout, currency, reviewsCount, averageRating, ordersByStatus));
    }

    public sealed record SellerDashboard(
        int OrdersTotal,
        int OrdersToProcess,
        decimal GrossSales30d,
        decimal NetPayout30d,
        string Currency,
        int ReviewsCount,
        double AverageRating,
        IReadOnlyDictionary<string, int> OrdersByStatus);
}
