using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// À la confirmation d'une commande, comptabilise le gain de chaque ligne pour
/// son vendeur : brut = (prix de base − réduction vendeur) × quantité ;
/// commission résolue via Billing (par catégorie) ; net = brut − commission.
/// Idempotent par commande. C'est l'alimentation du grand livre des reversements.
///
/// Alimente aussi les portefeuilles : le NET va au « solde à venir » de chaque
/// vendeur, la commission au solde commission de la plateforme, et les frais de
/// livraison au solde livraison de la plateforme.
/// </summary>
public sealed class AccrueEarningsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly ICatalogModuleApi _catalogModuleApi;
    private readonly ICommissionModuleApi _commissionModuleApi;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public AccrueEarningsOnOrderConfirmedHandler(
        ISellerEarningRepository earningRepository,
        IOrderingModuleApi orderingModuleApi,
        ICatalogModuleApi catalogModuleApi,
        ICommissionModuleApi commissionModuleApi,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork)
    {
        _earningRepository = earningRepository;
        _orderingModuleApi = orderingModuleApi;
        _catalogModuleApi = catalogModuleApi;
        _commissionModuleApi = commissionModuleApi;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (await _earningRepository.ExistsForOrderAsync(integrationEvent.OrderId, cancellationToken))
        {
            return;
        }

        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (order is null || order.Lines.Count == 0)
        {
            return;
        }

        var netBySeller = new Dictionary<Guid, decimal>();
        var totalCommission = 0m;

        foreach (var line in order.Lines)
        {
            var gross = (line.UnitBasePrice - line.SellerDiscount) * line.Quantity;
            if (gross < 0m)
            {
                gross = 0m;
            }

            var product = await _catalogModuleApi.GetProductAsync(line.ProductId, cancellationToken);
            var categoryId = product?.CategoryId ?? Guid.Empty;

            var commission = await _commissionModuleApi.ComputeCommissionAsync(
                line.SellerId, categoryId, gross, order.Currency, cancellationToken);

            var earning = SellerEarning.Create(
                order.Id, line.OfferId, line.SellerId, line.ProductId, gross, commission.CommissionAmount, order.Currency);

            if (earning.IsSuccess)
            {
                await _earningRepository.AddAsync(earning.Value, cancellationToken);

                netBySeller[line.SellerId] = netBySeller.GetValueOrDefault(line.SellerId) + earning.Value.NetAmount;
                totalCommission += earning.Value.CommissionAmount;
            }
        }

        // Portefeuille vendeur : le net va au solde à venir (libéré à la livraison).
        foreach (var (sellerId, net) in netBySeller)
        {
            await _wallets.CreditSellerPendingAsync(sellerId, net, order.Currency, order.Id, cancellationToken);
        }

        // Portefeuille plateforme : commission encaissée + frais de livraison encaissés.
        await _wallets.CreditPlatformCommissionAsync(totalCommission, order.Currency, order.Id, cancellationToken);
        await _wallets.CreditPlatformShippingAsync(order.ShippingFee, order.Currency, order.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
