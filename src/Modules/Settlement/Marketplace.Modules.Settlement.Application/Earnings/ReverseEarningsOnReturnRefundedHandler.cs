using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;

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
/// La contre-passation se fait AU PRORATA DE L'ÉCRITURE D'ORIGINE, et non par un
/// nouveau calcul à partir de taux. C'est plus qu'une simplification : recalculer
/// supposait que le barème n'avait pas bougé depuis la vente. S'il avait changé, on
/// reprenait au vendeur un montant qui ne correspondait à rien de ce qui lui avait
/// été crédité, et l'écart restait au grand livre. Défaire au prorata défait
/// exactement ce qui a été fait, quel que soit le barème du jour.
/// </para>
/// <para>
/// AUCUNE ÉCRITURE D'ORIGINE, AUCUNE CONTRE-PASSATION. Si le gain correspondant est
/// introuvable, le vendeur n'a jamais été crédité pour cette commande : le débiter
/// créerait la perte que ce handler est censé éviter. On journalise et on s'arrête.
/// </para>
public sealed class ReverseEarningsOnReturnRefundedHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly WalletMutations _wallets;
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<ReverseEarningsOnReturnRefundedHandler> _logger;

    public ReverseEarningsOnReturnRefundedHandler(
        WalletMutations wallets,
        ISellerEarningRepository earningRepository,
        ISettlementUnitOfWork unitOfWork,
        ILogger<ReverseEarningsOnReturnRefundedHandler> logger)
    {
        _wallets = wallets;
        _earningRepository = earningRepository;
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

        // ═════════════════════════════════════════════════════════════════════════
        // ON DÉFAIT L'ÉCRITURE D'ORIGINE, ON N'EN RECALCULE PAS UNE NOUVELLE.
        //
        // Les gains de ce vendeur sur cette commande portent déjà la répartition
        // exacte qui a été créditée à la confirmation. Le remboursement en reprend
        // la même fraction — aucun taux n'intervient, donc aucune divergence
        // possible si le barème a changé entre la vente et le retour.
        // ═════════════════════════════════════════════════════════════════════════
        var earnings = (await _earningRepository.ListByOrderAsync(e.OrderId, cancellationToken))
            .Where(x => x.SellerId == e.SellerId)
            .ToList();

        var accruedGross = earnings.Sum(x => x.GrossAmount);
        if (earnings.Count == 0 || accruedGross <= 0m)
        {
            _logger.LogError(
                "Remboursement {ReturnRequestId} (commande {OrderId}, vendeur {SellerId}) : AUCUN GAIN COMPTABILISÉ " +
                "à contre-passer. Rien n'est débité — débiter un vendeur jamais crédité créerait la perte " +
                "que cette contre-passation doit éviter. À traiter à la main.",
                e.ReturnRequestId, e.OrderId, e.SellerId);
            return;
        }

        // Même précision qu'à l'accrual : la devise du remboursement, pas l'unité en dur.
        var (commission, providerFee, sellerNet) = GrossSplit.Prorate(
            e.RefundAmount,
            accruedGross,
            earnings.Sum(x => x.CommissionAmount),
            earnings.Sum(x => x.ProviderFeeAmount),
            e.Currency);

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
