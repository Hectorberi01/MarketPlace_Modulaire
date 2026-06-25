using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Batches;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — finances : relevé de la boutique sur une période (ventes brutes,
/// commissions, net à reverser). Scopé à la boutique du jeton.
/// </summary>
public static class SellerFinanceEndpoints
{
    public static IEndpointRouteBuilder MapSellerFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var finance = app.MapGroup("/seller/finance").WithTags("Vendeur · Finances").RequireAuthorization();

        finance.MapGet("/statement", StatementAsync);

        // Détail des reversements (payouts) du vendeur : nécessite une query
        // Settlement filtrée par vendeur (aujourd'hui seuls les lots globaux et le
        // relevé agrégé sont exposés).
        finance.MapGet("/payouts", () => SellerBff.NotImplemented("settlement.seller_payouts"));

        return app;
    }

    /// <summary>
    /// Relevé sur une période (?from=&amp;to=, ISO-8601). Par défaut : les 30
    /// derniers jours. Renvoie ventes brutes, commissions et net à reverser.
    /// </summary>
    private static async Task<IResult> StatementAsync(
        ClaimsPrincipal user, DateTime? from, DateTime? to, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-30);
        if (end <= start)
        {
            return Results.BadRequest(new { error = "invalid_period", hint = "'to' doit être postérieur à 'from'." });
        }

        return (await sender.Send(new GetSellerStatementQuery(seller.Id, start, end), ct)).Match(s => Results.Ok(s));
    }
}
