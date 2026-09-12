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
        var finance = app.MapSellerGroup("/seller/finance", "Vendeur · Finances");

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
            // `message` EN PLUS DU CODE : les consoles affichent le premier champ
            // textuel qu'elles trouvent, et `hint` n'en fait pas partie. Sans lui, le
            // vendeur lisait « invalid_period » dans un bandeau d'erreur.
            return Results.BadRequest(new
            {
                error = "invalid_period",
                hint = "'to' doit être postérieur à 'from'.",
                message = "La date de fin doit être postérieure à la date de début.",
            });
        }

        // On récupère les LIGNES détaillées (un gain = une ligne) puis on projette
        // vers la forme EXACTE attendue par le dashboard Blazor : from/to, agrégats
        // XOF entiers (le XOF n'a pas de décimales) et lines[] signées.
        //
        // Les remboursements ne vivent PAS dans le grand livre Earnings (montants
        // toujours positifs). La source de vérité est le module Returns/RMA : un
        // retour au statut Refunded porte un RefundAmount et une date de résolution.
        // ─────────────────────────────────────────────────────────────────────────
        // UN RELEVÉ INCOMPLET VAUT MIEUX PAS DU TOUT QU'UN RELEVÉ FAUX.
        //
        // L'échec retombait ici sur une liste vide : `refundsXof` valait 0, les lignes
        // « refund » disparaissaient, et le vendeur lisait un net à percevoir
        // SUREVALUÉ — sans le moindre signal. Il conteste ensuite son versement, et
        // personne ne peut expliquer l'écart, puisque la panne n'a laissé aucune trace
        // visible.
        //
        // On refuse donc de servir un relevé partiel : 503, le client réessaie.
        // ─────────────────────────────────────────────────────────────────────────
        var refundsResult = await sender.Send(new ListSellerRefundsQuery(seller.Id, start, end), ct);
        if (refundsResult.IsFailure)
        {
            return Results.Problem(
                title: "sellers.finance.refunds_unavailable",
                detail: "Les remboursements de la période n'ont pas pu être récupérés. "
                      + "Le relevé n'est pas affiché pour ne pas présenter un solde inexact. Réessayez.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var refunds = refundsResult.Value;

        return (await sender.Send(new GetSellerStatementLinesQuery(seller.Id, start, end), ct)).Match(earnings =>
        {
            var grossSalesXof = (int)Math.Round(earnings.Sum(e => e.GrossAmount));
            var commissionXof = (int)Math.Round(earnings.Sum(e => e.CommissionAmount));
            var providerFeeXof = (int)Math.Round(earnings.Sum(e => e.ProviderFeeAmount));
            var refundsXof = (int)Math.Round(refunds.Sum(r => r.RefundAmount));

            // ═════════════════════════════════════════════════════════════════════
            // LE NET DES VENTES EST DÉSORMAIS PROJETÉ. IL ÉTAIT CALCULÉ, PUIS JETÉ.
            //
            // `SellerEarning.NetAmount` est le montant RÉELLEMENT crédité au vendeur
            // (`SellerEarning.cs:52`) : brut moins commission moins frais. La requête
            // le renvoie déjà par ligne (`SettlementQueries.cs:82`) ; ce BFF ne
            // projetait que les trois composantes, et l'écran recomposait la
            // soustraction lui-même.
            //
            // Or la recomposition était FAUSSE dès qu'une vente était remboursée :
            // rembourser ne retire pas la ligne de gain — `ListSellerEarningsAsync`
            // ne filtre sur aucun statut, et la contre-passation n'écrit jamais
            // `EarningStatus.Reversed`. La vente gardait donc son brut ET sa
            // commission au relevé, pendant que le remboursement était soustrait à
            // 100 %. Vente 10 000 / commission 1 000 / frais 500 remboursée
            // intégralement : impact réel sur le portefeuille = 0, chiffre affiché
            // = −1 500 F CFA. Le vendeur lisait une perte qu'il ne pouvait rapprocher
            // d'aucun mouvement.
            //
            // CE QUI N'EST PAS PROJETÉ, ET POURQUOI : la part nette REPRISE sur un
            // remboursement. `ReverseEarningsOnReturnRefundedHandler` la calcule par
            // prorata (`GrossSplit.Prorate`) au moment de la contre-passation, mais
            // `SellerRefundLine` ne porte que le montant brut rendu à l'acheteur — le
            // module Retours ignore le barème. L'inventer ici serait remplacer un
            // chiffre faux par un autre ; le portefeuille, lui, porte le débit exact.
            // ═════════════════════════════════════════════════════════════════════
            var netSalesXof = (int)Math.Round(earnings.Sum(e => e.NetAmount));

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
                // Ce qui a été crédité au vendeur pour ses ventes de la période.
                netSalesXof,
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
