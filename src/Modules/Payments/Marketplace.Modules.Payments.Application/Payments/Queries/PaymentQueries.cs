using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Queries;

/// <summary>Récupère un paiement par son identifiant.</summary>
public sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentSummary>;

/// <summary>Récupère le paiement d'une commande.</summary>
public sealed record GetPaymentByOrderQuery(Guid OrderId) : IQuery<PaymentSummary>;

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
