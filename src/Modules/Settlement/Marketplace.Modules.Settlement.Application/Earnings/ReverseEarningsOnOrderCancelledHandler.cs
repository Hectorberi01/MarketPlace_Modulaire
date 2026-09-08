using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// Reprend au vendeur et à la plateforme ce qu'une commande ANNULÉE leur avait rapporté
/// — SI elle leur avait rapporté quelque chose.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DANS LE MONDE D'AUJOURD'HUI, CE HANDLER NE FAIT RIEN. C'EST NORMAL, ET C'EST
/// PRÉCISÉMENT POURQUOI IL EXISTE.
///
/// Les gains sont comptabilisés à la CONFIRMATION, et le domaine refuse d'annuler une
/// commande confirmée (Order.Cancel). Une commande annulée n'a donc jamais rien crédité
/// à personne : le balayage des commandes abandonnées ne peut pas laisser un vendeur
/// payé pour une vente qui n'a pas eu lieu.
///
/// Mais cette garantie ne tient qu'à une règle du domaine, et les règles du domaine
/// changent. Le jour où une commande deviendra annulable après confirmation — un litige,
/// une rupture constatée après coup, une annulation admin — le vendeur garderait son
/// gain en silence, exactement comme il gardait son gain sur les retours remboursés
/// avant qu'on ne s'en aperçoive. Cela avait coûté de l'argent à chaque retour.
///
/// Le handler pose donc la question à chaque annulation, et ne fait le travail que si la
/// réponse est oui. Le coût est d'un EXISTS par commande annulée ; le bénéfice est
/// qu'aucune évolution future ne peut rouvrir ce trou sans qu'on l'ait déjà bouché.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ReverseEarningsOnOrderCancelledHandler : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IOrderingModuleApi _orders;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<ReverseEarningsOnOrderCancelledHandler> _logger;

    public ReverseEarningsOnOrderCancelledHandler(
        ISellerEarningRepository earningRepository,
        IOrderingModuleApi orders,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork,
        ILogger<ReverseEarningsOnOrderCancelledHandler> logger)
    {
        _earningRepository = earningRepository;
        _orders = orders;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // LA QUESTION D'HECTOR, POSÉE AU BON ENDROIT : cette commande a-t-elle été
        // créditée quelque part ? Dans le cas courant — une commande abandonnée avant
        // paiement — la réponse est non, et on s'arrête là sans écrire une ligne.
        var earnings = await _earningRepository.ListByOrderAsync(e.OrderId, cancellationToken);
        if (earnings.Count == 0)
        {
            return;
        }

        // VERROU DE REJEU. L'outbox livre au moins une fois : sans ce garde, un rejeu
        // débiterait le vendeur une seconde fois pour la même annulation.
        if (await _wallets.HasLedgerEntryAsync(
                WalletMutations.OrderCancelledReferenceType, e.OrderId, cancellationToken))
        {
            _logger.LogInformation(
                "Commande {OrderId} : contre-passation d'annulation DÉJÀ effectuée — rejeu ignoré.", e.OrderId);
            return;
        }

        var currency = earnings[0].Currency;

        // On défait EXACTEMENT ce qui a été écrit : les montants sont relus sur les gains
        // comptabilisés, aucun taux n'intervient. Recalculer exposerait au même défaut
        // que la contre-passation des retours — un barème modifié entre-temps reprendrait
        // au vendeur un montant qui ne correspond à rien de ce qu'il avait reçu.
        foreach (var group in earnings.GroupBy(x => x.SellerId))
        {
            await _wallets.DebitSellerForCancellationAsync(
                group.Key, group.Sum(x => x.NetAmount), currency, e.OrderId, cancellationToken);
        }

        // Les frais de livraison ne figurent pas sur les gains vendeur : ils ont été
        // encaissés par la plateforme depuis la commande. On les relit là où ils sont.
        var order = await _orders.GetOrderAsync(e.OrderId, cancellationToken);

        await _wallets.DebitPlatformForCancellationAsync(
            earnings.Sum(x => x.CommissionAmount),
            earnings.Sum(x => x.ProviderFeeAmount),
            order?.ShippingFee ?? 0m,
            currency,
            e.OrderId,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Commande {OrderId} annulée APRÈS avoir été comptabilisée : {Count} gain(s) contre-passé(s). " +
            "Ce cas n'est pas censé se produire aujourd'hui — vérifier quel chemin a annulé une commande confirmée.",
            e.OrderId, earnings.Count);
    }
}
