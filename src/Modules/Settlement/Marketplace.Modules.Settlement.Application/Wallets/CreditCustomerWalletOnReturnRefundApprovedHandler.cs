using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Rembourse un retour EN CRÉDITANT LA CAGNOTTE du client, puis annonce que c'est fait.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER SUPPRIME UN GESTE HUMAIN, ET C'EST TOUT SON INTÉRÊT.
///
/// Jusqu'ici, « remboursement validé » ne déclenchait rien de comptable : la demande
/// entrait dans une file d'attente admin, quelqu'un devait ouvrir le tableau de bord
/// FedaPay, effectuer le versement, puis revenir coller la référence dans la console.
/// FedaPay n'expose aucune API de remboursement — le geste manuel n'était pas une
/// paresse, c'était la seule voie possible pour faire SORTIR de l'argent.
///
/// Mais un remboursement n'a pas à sortir. Créditer une cagnotte interne rembourse le
/// client immédiatement, sans frais de versement, et laisse le retrait — lui, toujours
/// manuel côté PSP — se produire plus tard, à la demande, s'il le souhaite. Le client
/// est remboursé en quelques secondes au lieu de quelques jours, et la file d'attente
/// qui devait être vide chaque soir n'a plus lieu d'être.
///
/// Ce que la plateforme conserve en trésorerie, elle le doit toujours : la cagnotte
/// EST cette dette, et le grand livre en porte la trace ligne à ligne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CreditCustomerWalletOnReturnRefundApprovedHandler
    : IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>
{
    private readonly WalletMutations _wallets;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<CreditCustomerWalletOnReturnRefundApprovedHandler> _logger;

    public CreditCustomerWalletOnReturnRefundApprovedHandler(
        WalletMutations wallets,
        IIntegrationEventPublisher publisher,
        ISettlementUnitOfWork unitOfWork,
        ILogger<CreditCustomerWalletOnReturnRefundApprovedHandler> logger)
    {
        _wallets = wallets;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        ReturnRefundApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.RefundAmount <= 0m)
        {
            return;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
        //
        // L'outbox livre AT-LEAST-ONCE et rejoue TOUS les handlers d'un message dès
        // que l'un d'eux échoue. Sans ce garde, un rejeu créditerait le client une
        // seconde fois — de l'argent créé à partir de rien, et impossible à reprendre
        // une fois retiré.
        //
        // Le grand livre sert de registre : une écriture « return_refund » portant
        // l'identifiant de ce retour signifie que la cagnotte est déjà créditée.
        // ─────────────────────────────────────────────────────────────────────────
        if (await _wallets.CustomerAlreadyRefundedAsync(e.ReturnRequestId, cancellationToken))
        {
            _logger.LogInformation(
                "Remboursement {ReturnRequestId} : cagnotte DÉJÀ créditée — rejeu ignoré.",
                e.ReturnRequestId);

            // On republie malgré tout : si le rejeu vient de ce que Returns n'a jamais
            // reçu l'annonce, s'arrêter ici laisserait la demande éternellement « en
            // attente de versement » alors que le client a son argent. Republier est
            // sans danger — Returns ignore une confirmation déjà appliquée.
            await PublishCreditedAsync(e, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        await _wallets.CreditCustomerRefundAsync(
            e.BuyerId, e.RefundAmount, e.Currency, e.ReturnRequestId, cancellationToken);

        // L'écriture de cagnotte et le message qui l'annonce sont commités ENSEMBLE :
        // la publication passe par l'outbox du module, dans la même transaction. Aucun
        // des deux ne peut exister sans l'autre.
        await PublishCreditedAsync(e, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Remboursement {ReturnRequestId} : {Amount} {Currency} crédités sur la cagnotte du client {CustomerId}.",
            e.ReturnRequestId, e.RefundAmount, e.Currency, e.BuyerId);
    }

    private async Task PublishCreditedAsync(
        ReturnRefundApprovedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetOrCreateAsync(
            WalletOwnerType.Customer, e.BuyerId, e.Currency, cancellationToken);

        await _publisher.PublishAsync(
            new CustomerRefundCreditedIntegrationEvent
            {
                ReturnRequestId = e.ReturnRequestId,
                CustomerId = e.BuyerId,
                Amount = e.RefundAmount,
                Currency = e.Currency,
                LedgerReference = $"wallet:{wallet.Id.Value}"
            },
            cancellationToken);
    }
}
