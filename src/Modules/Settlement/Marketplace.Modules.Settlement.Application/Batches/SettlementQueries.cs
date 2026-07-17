using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Batches;

/// <summary>Récupère un lot de reversement.</summary>
public sealed record GetSettlementBatchQuery(Guid BatchId) : IQuery<SettlementBatchSummary>;

/// <summary>Liste les lots de reversement.</summary>
public sealed record ListSettlementBatchesQuery : IQuery<IReadOnlyList<SettlementBatchSummary>>;

/// <summary>Relevé d'un vendeur sur une période (ventes, commissions, net).</summary>
public sealed record GetSellerStatementQuery(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc) : IQuery<SellerStatementSummary>;

/// <summary>Lignes détaillées du relevé d'un vendeur sur une période (un gain = une ligne), triées par date.</summary>
public sealed record GetSellerStatementLinesQuery(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc) : IQuery<IReadOnlyList<SellerStatementLine>>;

internal static class SettlementMapper
{
    public static SettlementBatchSummary ToSummary(SettlementBatch b) => new(
        b.Id.Value, b.PeriodStartUtc, b.PeriodEndUtc, b.Currency, b.TotalNet, b.Status.ToString(), b.CreatedAtUtc,
        b.Payouts.Select(p => new PayoutSummary(
            p.Id, p.SellerId, p.GrossAmount, p.CommissionAmount, p.NetAmount, p.Currency, p.Status.ToString(), p.ProviderRef, p.PaidAtUtc)).ToList());
}

internal sealed class GetSettlementBatchQueryHandler : IQueryHandler<GetSettlementBatchQuery, SettlementBatchSummary>
{
    private readonly ISettlementBatchRepository _repository;

    public GetSettlementBatchQueryHandler(ISettlementBatchRepository repository) => _repository = repository;

    public async Task<Result<SettlementBatchSummary>> Handle(GetSettlementBatchQuery query, CancellationToken cancellationToken)
    {
        var batch = await _repository.GetByIdAsync(new SettlementBatchId(query.BatchId), cancellationToken);
        return batch is null
            ? Error.NotFound("settlement.batch.not_found", "Lot introuvable.")
            : SettlementMapper.ToSummary(batch);
    }
}

internal sealed class ListSettlementBatchesQueryHandler : IQueryHandler<ListSettlementBatchesQuery, IReadOnlyList<SettlementBatchSummary>>
{
    private readonly ISettlementBatchRepository _repository;

    public ListSettlementBatchesQueryHandler(ISettlementBatchRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SettlementBatchSummary>>> Handle(ListSettlementBatchesQuery query, CancellationToken cancellationToken)
    {
        var batches = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<SettlementBatchSummary> summaries = batches.Select(SettlementMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetSellerStatementQueryHandler : IQueryHandler<GetSellerStatementQuery, SellerStatementSummary>
{
    private readonly ISellerEarningRepository _repository;

    public GetSellerStatementQueryHandler(ISellerEarningRepository repository) => _repository = repository;

    public async Task<Result<SellerStatementSummary>> Handle(GetSellerStatementQuery query, CancellationToken cancellationToken)
    {
        var s = await _repository.GetSellerStatementAsync(query.SellerId, query.PeriodStartUtc, query.PeriodEndUtc, cancellationToken);
        return new SellerStatementSummary(s.SellerId, s.GrossSales, s.Commissions, s.NetPayout, s.Currency, s.LineCount);
    }
}

internal sealed class GetSellerStatementLinesQueryHandler : IQueryHandler<GetSellerStatementLinesQuery, IReadOnlyList<SellerStatementLine>>
{
    private readonly ISellerEarningRepository _repository;

    public GetSellerStatementLinesQueryHandler(ISellerEarningRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SellerStatementLine>>> Handle(GetSellerStatementLinesQuery query, CancellationToken cancellationToken)
    {
        var earnings = await _repository.ListSellerEarningsAsync(query.SellerId, query.PeriodStartUtc, query.PeriodEndUtc, cancellationToken);
        IReadOnlyList<SellerStatementLine> lines = earnings
            .Select(e => new SellerStatementLine(
                e.Id.Value, e.OrderId, e.CreatedAtUtc, e.GrossAmount, e.CommissionAmount, e.ProviderFeeAmount, e.NetAmount, e.Currency, e.Status.ToString()))
            .ToList();
        return Result.Success(lines);
    }
}
