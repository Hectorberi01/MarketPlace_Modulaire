using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// À la livraison confirmée, libère les gains de la commande (escrow levé) : ils
/// passent de « Accrued » à « Released » et deviennent éligibles au prochain lot
/// de reversement. Idempotent (un gain déjà libéré n'est pas modifié).
/// </summary>
public sealed class ReleaseEarningsOnOrderDeliveredHandler : IIntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ReleaseEarningsOnOrderDeliveredHandler(ISellerEarningRepository earningRepository, ISettlementUnitOfWork unitOfWork)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (earnings.Count == 0)
        {
            return;
        }

        foreach (var earning in earnings)
        {
            earning.Release();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
