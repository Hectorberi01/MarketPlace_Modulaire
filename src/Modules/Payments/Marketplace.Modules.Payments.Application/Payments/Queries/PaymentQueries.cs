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

/// <summary>
/// Agrégats de paiements (montants + compteurs) pour les indicateurs de la console.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// DES BORNES, ET NON UN NOMBRE DE JOURS.
///
/// Un « nombre de jours » ne sait pas exprimer « les douze mois CLOS » — celle des
/// quatre fenêtres du tableau de bord qui exclut le mois en cours. Compter 365 jours
/// glissants aurait mis dans le camembert jusqu'à un mois entier de données que la
/// courbe voisine, elle, laissait dehors : deux chiffres sous une même étiquette.
///
/// L'appelant envoie donc les mêmes bornes qu'au rapport analytique. Nulles = tout
/// l'historique, ce qui reste le comportement des appelants qui ne demandent rien.
/// <paramref name="ToUtc"/> est EXCLUSIVE.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record GetPaymentStatsQuery(
    string? Search = null, DateOnly? FromUtc = null, DateOnly? ToUtc = null) : IQuery<PaymentStatsSummary>;

internal sealed class GetPaymentStatsQueryHandler : IQueryHandler<GetPaymentStatsQuery, PaymentStatsSummary>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentStatsQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<PaymentStatsSummary>> Handle(GetPaymentStatsQuery query, CancellationToken cancellationToken)
    {
        Guid? id = Guid.TryParse(query.Search, out var g) ? g : null;

        // `DateOnly` → `DateTime` en UTC EXPLICITE : les colonnes sont en
        // `timestamp with time zone`, et Npgsql refuse un DateTime de Kind Unspecified.
        // Sans cela la requête levait au lieu de filtrer.
        DateTime? debut = query.FromUtc is { } d
            ? DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : null;
        DateTime? fin = query.ToUtc is { } f
            ? DateTime.SpecifyKind(f.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : null;

        var s = await _repository.GetStatsAsync(id, debut, fin, cancellationToken);
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
