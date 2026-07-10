using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Returns.Application.Returns;

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
        finance.MapGet("/payouts", PayoutsAsync);

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

        // Les from/to liés depuis la query string sont en Kind=Unspecified ; les
        // colonnes PostgreSQL sont en « timestamp with time zone » (Npgsql exige
        // du DateTimeKind.Utc). On normalise donc en UTC avant toute requête.
        var end = ToUtc(to) ?? DateTime.UtcNow;
        var start = ToUtc(from) ?? end.AddDays(-30);
        if (end <= start)
        {
            return Results.BadRequest(new { error = "invalid_period", hint = "'to' doit être postérieur à 'from'." });
        }

        // On récupère les LIGNES détaillées (un gain = une ligne) puis on projette
        // vers la forme EXACTE attendue par le dashboard Blazor : from/to, agrégats
        // XOF entiers (le XOF n'a pas de décimales) et lines[] signées.
        //
        // Les remboursements ne vivent PAS dans le grand livre Earnings (montants
        // toujours positifs). La source de vérité est le module Returns/RMA : un
        // retour au statut Refunded porte un RefundAmount et une date de résolution.
        var refundsResult = await sender.Send(new ListSellerRefundsQuery(seller.Id, start, end), ct);
        IReadOnlyList<Marketplace.Modules.Returns.Contracts.SellerRefundLine> refunds =
            refundsResult.IsSuccess ? refundsResult.Value : Array.Empty<Marketplace.Modules.Returns.Contracts.SellerRefundLine>();

        return (await sender.Send(new GetSellerStatementLinesQuery(seller.Id, start, end), ct)).Match(earnings =>
        {
            var grossSalesXof = (int)Math.Round(earnings.Sum(e => e.GrossAmount));
            var commissionXof = (int)Math.Round(earnings.Sum(e => e.CommissionAmount));
            var providerFeeXof = (int)Math.Round(earnings.Sum(e => e.ProviderFeeAmount));
            var refundsXof = (int)Math.Round(refunds.Sum(r => r.RefundAmount));

            // Pour chaque gain : une écriture « vente » (+brut), une écriture
            // « commission plateforme » (−commission) ET, si applicable, une écriture
            // « frais provider » (−frais). Chaque remboursement devient une écriture
            // « refund » (−montant). Le tout trié chronologiquement.
            var lines = earnings
                .SelectMany(e =>
                {
                    var entries = new List<StatementLineDto>
                    {
                        new(e.CreatedAtUtc, $"Vente commande #{e.OrderId}", "sale", (int)Math.Round(e.GrossAmount)),
                        new(e.CreatedAtUtc, "Commission plateforme", "commission", -(int)Math.Round(e.CommissionAmount)),
                    };
                    if (e.ProviderFeeAmount > 0)
                    {
                        entries.Add(new StatementLineDto(e.CreatedAtUtc, $"Frais provider commande #{e.OrderId}", "provider", -(int)Math.Round(e.ProviderFeeAmount)));
                    }
                    return entries;
                })
                .Concat(refunds.Select(r =>
                    new StatementLineDto(r.RefundedAtUtc, $"Remboursement commande #{r.OrderId}", "refund", -(int)Math.Round(r.RefundAmount))))
                .OrderBy(l => l.Date)
                .ToList();

            return Results.Ok(new
            {
                from = start,
                to = end,
                grossSalesXof,
                commissionXof,
                // Somme des frais provider du vendeur sur la période.
                providerFeeXof,
                // Somme des remboursements du vendeur (retours Refunded) sur la période.
                refundsXof,
                lines,
            });
        });
    }

    /// <summary>
    /// Ligne du relevé projetée vers la forme du dashboard : date, libellé, type
    /// (sale|commission|refund|payout) et montant signé en XOF (entier).
    /// </summary>
    private sealed record StatementLineDto(DateTime Date, string Label, string Type, int AmountXof);

    /// <summary>Normalise une date en UTC (les colonnes sont « timestamptz », Npgsql exige Kind=Utc).</summary>
    private static DateTime? ToUtc(DateTime? value)
        => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>Détail des reversements (payouts) de la boutique du jeton, tous lots confondus.</summary>
    private static async Task<IResult> PayoutsAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListSellerPayoutsQuery(seller.Id), ct)).Match(p => Results.Ok(p));
    }
}
