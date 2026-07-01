using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// À la livraison confirmée de TOUTE la commande, libère les gains restants
/// (escrow levé) : passage « Accrued » → « Released », et déplacement du net
/// correspondant du solde à venir vers le solde principal de chaque vendeur.
/// Filet de sécurité complémentaire du handler par expédition : idempotent, il
/// ne traite que les gains encore « Accrued » (les autres ont déjà été déplacés).
/// </summary>
public sealed class ReleaseEarningsOnOrderDeliveredHandler : IIntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly WalletMutations _wallets;

    public ReleaseEarningsOnOrderDeliveredHandler(
        ISellerEarningRepository earningRepository,
        ISettlementUnitOfWork unitOfWork,
        WalletMutations wallets)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
        _wallets = wallets;
    }

    public async Task HandleAsync(OrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (earnings.Count == 0)
        {
            return;
        }

        // Regroupe le net réellement libéré (Accrued → Released) par vendeur.
        var releasedNetBySeller = new Dictionary<Guid, decimal>();
        var currencyBySeller = new Dictionary<Guid, string>();
        foreach (var earning in earnings)
        {
            if (earning.Status == EarningStatus.Accrued)
            {
                earning.Release();
                releasedNetBySeller[earning.SellerId] = releasedNetBySeller.GetValueOrDefault(earning.SellerId) + earning.NetAmount;
                currencyBySeller[earning.SellerId] = earning.Currency;
            }
        }

        foreach (var (sellerId, net) in releasedNetBySeller)
        {
            await _wallets.ReleaseSellerAsync(sellerId, net, currencyBySeller[sellerId], integrationEvent.OrderId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
