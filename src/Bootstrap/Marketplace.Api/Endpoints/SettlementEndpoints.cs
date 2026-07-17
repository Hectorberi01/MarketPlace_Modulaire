using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Settlement.Application.Batches;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Settlement : génération des lots de reversement
/// (un payout net par vendeur) et suivi jusqu'au versement. Le grand livre des
/// gains est alimenté automatiquement à la confirmation des commandes. Back-office.
/// </summary>
public static class SettlementEndpoints
{
    /// <summary>Enregistre les routes de reversement.</summary>
    public static IEndpointRouteBuilder MapSettlementEndpoints(this IEndpointRouteBuilder app)
    {
        var settlement = app.MapAdminGroup("/api/settlement").WithTags("Settlement")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        settlement.MapPost("/run", RunAsync).WithName("RunSettlement");
        settlement.MapGet("/batches", ListBatchesAsync).WithName("ListSettlementBatches");
        settlement.MapGet("/batches/{id:guid}", GetBatchAsync).WithName("GetSettlementBatch");
        settlement.MapPost("/batches/{batchId:guid}/payouts/{payoutId:guid}/pay", PayAsync).WithName("MarkPayoutPaid");
        settlement.MapGet("/sellers/{sellerId:guid}/statement", StatementAsync).WithName("GetSellerStatement");

        return app;
    }

    /// <summary>Génère un lot de reversement pour la période donnée.</summary>
    private static async Task<IResult> RunAsync(RunRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RunSettlementCommand(request.PeriodStartUtc, request.PeriodEndUtc), ct);
        return result.Match(id => Results.Created($"/api/settlement/batches/{id}", new { id }));
    }

    /// <summary>Liste les lots de reversement.</summary>
    private static async Task<IResult> ListBatchesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListSettlementBatchesQuery(), ct);
        return result.Match(batches => Results.Ok(batches));
    }

    /// <summary>Récupère un lot de reversement et ses payouts.</summary>
    private static async Task<IResult> GetBatchAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetSettlementBatchQuery(id), ct);
        return result.Match(batch => Results.Ok(batch));
    }

    /// <summary>Marque un reversement comme versé (transfert MoMo/banque simulé).</summary>
    private static async Task<IResult> PayAsync(Guid batchId, Guid payoutId, PayRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkPayoutPaidCommand(batchId, payoutId, request.ProviderRef), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Relevé d'un vendeur sur une période (ventes brutes, commissions, net).</summary>
    private static async Task<IResult> StatementAsync(Guid sellerId, DateTime start, DateTime end, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetSellerStatementQuery(sellerId, start, end), ct);
        return result.Match(statement => Results.Ok(statement));
    }

    public sealed record RunRequest(DateTime PeriodStartUtc, DateTime PeriodEndUtc);
    public sealed record PayRequest(string ProviderRef);
}
