using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Pricing;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Modules.Settlement.Application.Earnings;

/// <summary>
/// Contre-passe les gains d'une vente REMBOURSÉE — argent réellement versé.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE HANDLER N'EXISTAIT PAS, ET SON ABSENCE COÛTAIT DE L'ARGENT À CHAQUE RETOUR.
///
/// `ReturnRefundedIntegrationEvent` était publié depuis le début — et n'avait AUCUN
/// consommateur. Résultat : un article revenait, le client était (censé être)
/// remboursé, et le vendeur gardait malgré tout son gain. La plateforme payait donc
/// DEUX FOIS : le remboursement au client, et la vente au vendeur.
///
/// La marchandise rentre, l'argent sort deux fois. Chaque retour était une perte
/// sèche, invisible dans les comptes.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// <para>
/// La répartition reprend EXACTEMENT la formule de l'accrual
/// (AccrueEarningsOnOrderConfirmedHandler) : le brut remboursé se décompose en
/// commission, frais provider et net vendeur. Utiliser une autre formule ici — même
/// « équivalente » — laisserait des centimes orphelins à chaque retour, et les
/// comptes cesseraient de tomber juste.
/// </para>
public sealed class ReverseEarningsOnReturnRefundedHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly WalletMutations _wallets;
    private readonly PricingOptions _pricing;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<ReverseEarningsOnReturnRefundedHandler> _logger;

    public ReverseEarningsOnReturnRefundedHandler(
        WalletMutations wallets,
        PricingOptions pricing,
        ISettlementUnitOfWork unitOfWork,
        ILogger<ReverseEarningsOnReturnRefundedHandler> logger)
    {
        _wallets = wallets;
        _pricing = pricing;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.RefundAmount <= 0m)
        {
            return;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
        //
        // L'outbox livre AT-LEAST-ONCE. Le dispatcher rejoue TOUS les handlers d'un
        // message dès que l'un d'eux échoue — y compris ceux qui avaient réussi.
        //
        // Sans ce garde, un simple rejeu débitait le vendeur une SECONDE fois et
        // restituait la commission deux fois. Une perte comptable réelle, silencieuse,
        // et survenant dans le code même censé fiabiliser les remboursements.
        //
        // Les voisins se protègent depuis toujours (AccrueEarnings → ExistsForOrder,
        // Loyalty → HasEarnedForOrder, Analytics → ProcessedOrder). Celui-ci ne le
        // faisait pas : c'était un oubli, pas un choix.
        //
        // Le grand livre sert de registre : s'il porte déjà une écriture pour ce
        // remboursement, la contre-passation a déjà eu lieu.
        // ─────────────────────────────────────────────────────────────────────────
        if (await _wallets.RefundAlreadyReversedAsync(e.ReturnRequestId, cancellationToken))
        {
            _logger.LogInformation(
                "Remboursement {ReturnRequestId} : contre-passation DÉJÀ effectuée — rejeu ignoré.",
                e.ReturnRequestId);
            return;
        }

        var commRate = _pricing.PlatformCommissionRate;
        var provRate = _pricing.ProviderFeeRate;
        var total = commRate + provRate;

        // Le montant remboursé est le BRUT (ce que l'acheteur avait payé). On le
        // redécompose comme à l'accrual : brut = net × (1 + commission + provider).
        var commission = Math.Round(e.RefundAmount * commRate / (1 + total));
        var providerFee = Math.Round(e.RefundAmount * provRate / (1 + total));

        // Le net vendeur est le RESTE, et non un troisième arrondi : c'est ce qui
        // garantit brut = commission + provider + net, au franc près. Trois arrondis
        // indépendants ne se rejoignent pas, et l'écart s'accumule silencieusement.
        var sellerNet = e.RefundAmount - commission - providerFee;

        // La référence est le REMBOURSEMENT, pas la commande : deux retours sur la même
        // commande doivent rester distinguables au grand livre — et c'est cette
        // référence qui sert de verrou au rejeu.
        await _wallets.DebitSellerForRefundAsync(e.SellerId, sellerNet, e.Currency, e.ReturnRequestId, cancellationToken);
        await _wallets.DebitPlatformCommissionAsync(commission, e.Currency, e.ReturnRequestId, cancellationToken);
        await _wallets.DebitPlatformProviderFeeAsync(providerFee, e.Currency, e.ReturnRequestId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Remboursement {ReturnRequestId} (réf. {RefundReference}) : contre-passation de {RefundAmount} {Currency} " +
            "— net vendeur {SellerNet} repris à {SellerId}, commission {Commission} et frais {ProviderFee} restitués.",
            e.ReturnRequestId, e.RefundReference, e.RefundAmount, e.Currency, sellerNet, e.SellerId, commission, providerFee);
    }
}
