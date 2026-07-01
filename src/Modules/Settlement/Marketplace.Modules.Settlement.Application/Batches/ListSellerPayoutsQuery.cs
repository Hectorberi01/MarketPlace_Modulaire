using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Batches;

namespace Marketplace.Modules.Settlement.Application.Batches;

/// <summary>
/// Liste les reversements (payouts) d'un vendeur, tous lots confondus.
/// </summary>
public sealed record ListSellerPayoutsQuery(Guid SellerId) : IQuery<IReadOnlyList<PayoutSummary>>;

internal sealed class ListSellerPayoutsQueryHandler
    : IQueryHandler<ListSellerPayoutsQuery, IReadOnlyList<PayoutSummary>>
{
    private readonly ISettlementBatchRepository _repository;

    public ListSellerPayoutsQueryHandler(ISettlementBatchRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<PayoutSummary>>> Handle(ListSellerPayoutsQuery query, CancellationToken cancellationToken)
    {
        var payouts = await _repository.ListPayoutsBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<PayoutSummary> summaries = payouts
            .Select(p => new PayoutSummary(
                p.Id, p.SellerId, p.GrossAmount, p.CommissionAmount, p.NetAmount,
                p.Currency, p.Status.ToString(), p.ProviderRef, p.PaidAtUtc))
            .ToList();
        return Result.Success(summaries);
    }
}
