using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Queries;

/// <summary>Récupère un paiement par son identifiant.</summary>
public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentSummary>;

/// <summary>Récupère le paiement d'une commande.</summary>
public sealed record GetPaymentByOrderQuery(Guid OrderId) : IQuery<PaymentSummary>;

/// <summary>Liste tous les paiements de la plateforme (back-office admin).</summary>
public sealed record ListPaymentsQuery : IQuery<IReadOnlyList<PaymentSummary>>;

internal sealed class ListPaymentsQueryHandler : IQueryHandler<ListPaymentsQuery, IReadOnlyList<PaymentSummary>>
{
    private readonly IPaymentRepository _repository;

    public ListPaymentsQueryHandler(IPaymentRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<PaymentSummary>>> Handle(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        var payments = await _repository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<PaymentSummary> summaries = payments.Select(PaymentMapper.ToSummary).ToList();
        return Result.Success(summaries);
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
