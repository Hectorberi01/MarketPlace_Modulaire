using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Pagination;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Queries;

/// <summary>Récupère un paiement par son identifiant.</summary>
public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentSummary>;

/// <summary>Récupère le paiement d'une commande.</summary>
public sealed record GetPaymentByOrderQuery(Guid OrderId) : IQuery<PaymentSummary>;

/// <summary>Agrégats de paiements (montants + compteurs) pour les indicateurs de la console.</summary>
public sealed record GetPaymentStatsQuery(string? Search = null) : IQuery<PaymentStatsSummary>;

internal sealed class GetPaymentStatsQueryHandler : IQueryHandler<GetPaymentStatsQuery, PaymentStatsSummary>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentStatsQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<PaymentStatsSummary>> Handle(GetPaymentStatsQuery query, CancellationToken cancellationToken)
    {
        Guid? id = Guid.TryParse(query.Search, out var g) ? g : null;
        var s = await _repository.GetStatsAsync(id, cancellationToken);
        return Result.Success(new PaymentStatsSummary(
            s.Total, s.CapturedCount, s.CapturedAmount, s.PendingCount, s.FailedCount, s.RefundedCount, s.RefundedAmount));
    }
}

/// <summary>Page de paiements pour la console admin (filtre statut, recherche par identifiant).</summary>
public sealed record ListPaymentsQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<PaymentSummary>>;

internal sealed class ListPaymentsQueryHandler : IQueryHandler<ListPaymentsQuery, PagedResult<PaymentSummary>>
{
    private readonly IPaymentRepository _repository;

    public ListPaymentsQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<PaymentSummary>>> Handle(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        PaymentStatus? status = Enum.TryParse<PaymentStatus>(query.Status, ignoreCase: true, out var parsed) ? parsed : null;
        Guid? id = Guid.TryParse(query.Search, out var g) ? g : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (payments, total, statusCounts) = await _repository.ListPagedAsync(page, pageSize, id, status, query.Sort, desc, cancellationToken);
        var items = payments.Select(PaymentMapper.ToSummary).ToList();
        return Result.Success(new PagedResult<PaymentSummary>(items, total, page, pageSize, statusCounts));
    }
}

internal sealed class GetPaymentQueryHandler : IQueryHandler<GetPaymentQuery, PaymentSummary>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<PaymentSummary>> Handle(GetPaymentQuery query, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(query.PaymentId), cancellationToken);
        return payment is null
            ? Error.NotFound("payments.not_found", "Paiement introuvable.")
            : PaymentMapper.ToSummary(payment);
    }
}

internal sealed class GetPaymentByOrderQueryHandler : IQueryHandler<GetPaymentByOrderQuery, PaymentSummary>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentByOrderQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<PaymentSummary>> Handle(GetPaymentByOrderQuery query, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByOrderAsync(query.OrderId, cancellationToken);
        return payment is null
            ? Error.NotFound("payments.not_found", "Aucun paiement pour cette commande.")
            : PaymentMapper.ToSummary(payment);
    }
}
