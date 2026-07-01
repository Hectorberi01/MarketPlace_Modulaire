using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// Affinage multi-vendeur : à la livraison de l'expédition d'UN vendeur, on libère
/// ses gains sur la commande (escrow levé) PUIS on déplace le NET correspondant de
/// son « solde à venir » vers son « solde principal » (retirable).
///
/// Aucun payout automatique : le vendeur déclenchera lui-même un retrait depuis
/// son solde principal. Idempotent : seuls les gains qui passent réellement de
/// Accrued à Released dans cet appel alimentent le déplacement de solde.
/// </summary>
public sealed class ReleaseSellerEarningsOnShipmentDeliveredHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly WalletMutations _wallets;

    public ReleaseSellerEarningsOnShipmentDeliveredHandler(
        ISellerEarningRepository earningRepository,
        ISettlementUnitOfWork unitOfWork,
        WalletMutations wallets)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
        _wallets = wallets;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        var sellerEarnings = earnings.Where(e => e.SellerId == integrationEvent.SellerId).ToList();
        if (sellerEarnings.Count == 0)
        {
            return;
        }

        // Ne déplace que le net qui bascule effectivement Accrued → Released ici.
        var releasedNet = 0m;
        var currency = sellerEarnings[0].Currency;
        foreach (var earning in sellerEarnings)
        {
            if (earning.Status == EarningStatus.Accrued)
            {
                earning.Release();
                releasedNet += earning.NetAmount;
            }
        }

        if (releasedNet > 0m)
        {
            await _wallets.ReleaseSellerAsync(integrationEvent.SellerId, releasedNet, currency, integrationEvent.OrderId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
