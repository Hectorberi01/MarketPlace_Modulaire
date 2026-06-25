using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.EventHandlers;

/// <summary>
/// À la livraison confirmée, libère l'escrow du paiement de la commande : les
/// fonds encaissés deviennent reversables au vendeur. Idempotent (sans effet si
/// déjà libéré, ou si le paiement n'est pas encaissé).
/// </summary>
public sealed class ReleaseEscrowOnOrderDeliveredHandler : IIntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public ReleaseEscrowOnOrderDeliveredHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetByOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Captured)
        {
            return;
        }

        var result = payment.ReleaseEscrow();
        if (result.IsSuccess)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
