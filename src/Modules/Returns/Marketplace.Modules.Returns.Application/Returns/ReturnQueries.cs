using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Returns.Contracts;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Application.Returns;

/// <summary>Récupère une demande de retour.</summary>
public sealed record GetReturnQuery(Guid ReturnRequestId) : IQuery<ReturnRequestSummary>;

/// <summary>Liste les retours d'un acheteur.</summary>
public sealed record ListMyReturnsQuery(Guid BuyerId) : IQuery<IReadOnlyList<ReturnRequestSummary>>;

/// <summary>Liste les retours d'un vendeur.</summary>
public sealed record ListSellerReturnsQuery(Guid SellerId) : IQuery<IReadOnlyList<ReturnRequestSummary>>;

internal static class ReturnMapper
{
    public static ReturnRequestSummary ToSummary(ReturnRequest r) => new(
        r.Id.Value, r.OrderId, r.OfferId, r.BuyerId, r.SellerId, r.Reason.ToString(), r.Status.ToString(),
        r.Currency, r.RefundAmount, r.Carrier, r.TrackingNumber, r.CreatedAtUtc, r.ResolvedAtUtc);
}

internal sealed class GetReturnQueryHandler : IQueryHandler<GetReturnQuery, ReturnRequestSummary>
{
    private readonly IReturnRequestRepository _repository;
    public GetReturnQueryHandler(IReturnRequestRepository repository) => _repository = repository;

    public async Task<Result<ReturnRequestSummary>> Handle(GetReturnQuery query, CancellationToken cancellationToken)
    {
        var request = await _repository.GetByIdAsync(new ReturnRequestId(query.ReturnRequestId), cancellationToken);
        return request is null
            ? Error.NotFound("returns.not_found", "Demande de retour introuvable.")
            : ReturnMapper.ToSummary(request);
    }
}

internal sealed class ListMyReturnsQueryHandler : IQueryHandler<ListMyReturnsQuery, IReadOnlyList<ReturnRequestSummary>>
{
    private readonly IReturnRequestRepository _repository;
    public ListMyReturnsQueryHandler(IReturnRequestRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ReturnRequestSummary>>> Handle(ListMyReturnsQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByBuyerAsync(query.BuyerId, cancellationToken);
        IReadOnlyList<ReturnRequestSummary> summaries = items.Select(ReturnMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListSellerReturnsQueryHandler : IQueryHandler<ListSellerReturnsQuery, IReadOnlyList<ReturnRequestSummary>>
{
    private readonly IReturnRequestRepository _repository;
    public ListSellerReturnsQueryHandler(IReturnRequestRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ReturnRequestSummary>>> Handle(ListSellerReturnsQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<ReturnRequestSummary> summaries = items.Select(ReturnMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
