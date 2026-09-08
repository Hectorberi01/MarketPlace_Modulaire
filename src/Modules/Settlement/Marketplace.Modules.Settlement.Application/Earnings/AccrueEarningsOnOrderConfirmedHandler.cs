using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// À la confirmation d'une commande, comptabilise le gain de chaque ligne pour
/// son vendeur. Nouveau modèle de prix : le brut d'une ligne (prix produit payé
/// par l'acheteur) = prix vendeur net × (1 + commission + provider). On répartit
/// donc le brut en commission = brut × commission/(1+total), frais provider =
/// brut × provider/(1+total), et net vendeur = le reste (brut − commission −
/// provider), ce qui garantit une somme exacte. Idempotent par commande.
///
/// Alimente aussi les portefeuilles : le NET va au « solde à venir » de chaque
/// vendeur, la commission au solde commission de la plateforme, les frais
/// provider au solde provider de la plateforme, et les frais de livraison au
/// solde livraison de la plateforme.
/// </summary>
public sealed class AccrueEarningsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public AccrueEarningsOnOrderConfirmedHandler(
        ISellerEarningRepository earningRepository,
        IOrderingModuleApi orderingModuleApi,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork)
    {
        _earningRepository = earningRepository;
        _orderingModuleApi = orderingModuleApi;
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
        var totalProvider = 0m;

        foreach (var line in order.Lines)
        {
            var gross = (line.UnitBasePrice - line.SellerDiscount) * line.Quantity;
            if (gross < 0m)
            {
                gross = 0m;
            }

            // ═════════════════════════════════════════════════════════════════════
            // LES TAUX VIENNENT DE LA LIGNE, PLUS DE LA CONFIGURATION.
            //
            // Le prix acheteur a été construit par l'offre comme
            //     prix_vendeur × (1 + commission + provider),
            // avec les taux en vigueur CE JOUR-LÀ. Refaire le partage avec les taux
            // d'aujourd'hui, c'est appliquer un barème à un prix qui n'a pas été
            // calculé avec lui : passer la commission de 10 % à 12 % faisait alors
            // toucher 9 830 à un vendeur qui avait fixé son net à 10 000, sur toutes
            // ses offres antérieures, et sans la moindre trace.
            //
            // La ligne porte désormais son propre barème. Un changement de taux
            // n'affecte plus que ce qui est vendu après lui.
            // ═════════════════════════════════════════════════════════════════════
            var (commission, providerFee, _) =
                GrossSplit.Split(gross, line.CommissionRate, line.ProviderFeeRate);

            var earning = SellerEarning.Create(
                order.Id, line.OfferId, line.SellerId, line.ProductId, gross, commission, providerFee, order.Currency);

            if (earning.IsSuccess)
            {
                await _earningRepository.AddAsync(earning.Value, cancellationToken);

                netBySeller[line.SellerId] = netBySeller.GetValueOrDefault(line.SellerId) + earning.Value.NetAmount;
                totalCommission += earning.Value.CommissionAmount;
                totalProvider += earning.Value.ProviderFeeAmount;
            }
        }

        // Portefeuille vendeur : le net va au solde à venir (libéré à la livraison).
        foreach (var (sellerId, net) in netBySeller)
        {
            await _wallets.CreditSellerPendingAsync(sellerId, net, order.Currency, order.Id, cancellationToken);
        }

        // Portefeuille plateforme : commission + frais provider + frais de livraison encaissés.
        await _wallets.CreditPlatformCommissionAsync(totalCommission, order.Currency, order.Id, cancellationToken);
        await _wallets.CreditPlatformProviderFeeAsync(totalProvider, order.Currency, order.Id, cancellationToken);
        await _wallets.CreditPlatformShippingAsync(order.ShippingFee, order.Currency, order.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
