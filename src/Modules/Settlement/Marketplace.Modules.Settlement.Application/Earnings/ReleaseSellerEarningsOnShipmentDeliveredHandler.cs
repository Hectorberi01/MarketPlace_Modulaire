using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// Affinage multi-vendeur : quand l'expédition d'UN vendeur est livrée, on libère
/// uniquement les gains de CE vendeur sur la commande (escrow levé, payable), sans
/// attendre la livraison des autres vendeurs. Complète la libération au niveau
/// commande (OrderDelivered). Idempotent.
/// </summary>
public sealed class ReleaseSellerEarningsOnShipmentDeliveredHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ReleaseSellerEarningsOnShipmentDeliveredHandler(ISellerEarningRepository earningRepository, ISettlementUnitOfWork unitOfWork)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        var sellerEarnings = earnings.Where(e => e.SellerId == integrationEvent.SellerId).ToList();
        if (sellerEarnings.Count == 0)
        {
            return;
        }

        foreach (var earning in sellerEarnings)
        {
            earning.Release();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
