using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Disputes.Contracts;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Application.Disputes;

/// <summary>Récupère un litige et son fil de messages.</summary>
public sealed record GetDisputeQuery(Guid DisputeId) : IQuery<DisputeSummary>;

/// <summary>Liste les litiges d'une commande.</summary>
public sealed record ListDisputesByOrderQuery(Guid OrderId) : IQuery<IReadOnlyList<DisputeSummary>>;

/// <summary>Liste les litiges où l'utilisateur est partie.</summary>
public sealed record ListMyDisputesQuery(Guid UserId) : IQuery<IReadOnlyList<DisputeSummary>>;

internal static class DisputeMapper
{
    public static DisputeSummary ToSummary(Dispute d) => new(
        d.Id.Value, d.OrderId, d.RaisedBy, d.Type.ToString(), d.Status.ToString(),
        d.Resolution?.ToString(), d.CreatedAtUtc, d.ResolvedAtUtc,
        d.Messages.OrderBy(m => m.CreatedAtUtc)
            .Select(m => new DisputeMessageSummary(m.AuthorId, m.Body, m.PhotoUrl, m.CreatedAtUtc)).ToList());
}

internal sealed class GetDisputeQueryHandler : IQueryHandler<GetDisputeQuery, DisputeSummary>
{
    private readonly IDisputeRepository _repository;
    public GetDisputeQueryHandler(IDisputeRepository repository) => _repository = repository;

    public async Task<Result<DisputeSummary>> Handle(GetDisputeQuery query, CancellationToken cancellationToken)
    {
        var dispute = await _repository.GetByIdAsync(new DisputeId(query.DisputeId), cancellationToken);
        return dispute is null
            ? Error.NotFound("disputes.not_found", "Litige introuvable.")
            : DisputeMapper.ToSummary(dispute);
    }
}

internal sealed class ListDisputesByOrderQueryHandler : IQueryHandler<ListDisputesByOrderQuery, IReadOnlyList<DisputeSummary>>
{
    private readonly IDisputeRepository _repository;
    public ListDisputesByOrderQueryHandler(IDisputeRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<DisputeSummary>>> Handle(ListDisputesByOrderQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByOrderAsync(query.OrderId, cancellationToken);
        IReadOnlyList<DisputeSummary> summaries = items.Select(DisputeMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListMyDisputesQueryHandler : IQueryHandler<ListMyDisputesQuery, IReadOnlyList<DisputeSummary>>
{
    private readonly IDisputeRepository _repository;
    public ListMyDisputesQueryHandler(IDisputeRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<DisputeSummary>>> Handle(ListMyDisputesQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByParticipantAsync(query.UserId, cancellationToken);
        IReadOnlyList<DisputeSummary> summaries = items.Select(DisputeMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
