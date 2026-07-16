using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Le remboursement est ACCEPTÉ — l'argent n'est pas encore parti.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE MESSAGE EST CELUI QUI ÉVITE LES LITIGES.
///
/// Entre la décision et le versement, il s'écoule des heures : un administrateur doit
/// exécuter l'opération dans le tableau de bord FedaPay, qui n'expose aucune API de
/// remboursement. Pendant ce délai, un client qui ne reçoit RIEN et qui ne sait RIEN
/// suppose le pire — et il a raison de le supposer.
///
/// On lui dit donc explicitement que sa demande est acceptée, et que le versement est
/// en cours. Le silence, ici, coûte plus cher qu'un remboursement.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class ReturnRefundApprovedNotificationHandler : IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public ReturnRefundApprovedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(ReturnRefundApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId,
            "Remboursement accepté",
            $"Votre remboursement de {e.RefundAmount:0.00} {e.Currency} est accepté. " +
            "Le versement est en cours ; vous serez prévenu dès qu'il sera effectué.",
            "Return",
            e.ReturnRequestId,
            cancellationToken);
}

/// <summary>
/// L'argent est PARTI. On prévient l'acheteur… et le vendeur, qui vient d'être débité.
/// </summary>
public sealed class ReturnRefundedNotificationHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<ReturnRefundedNotificationHandler> _logger;

    public ReturnRefundedNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<ReturnRefundedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'ACHETEUR d'abord : c'est lui qui attend son argent.
        await _dispatcher.NotifyAsync(
            e.BuyerId,
            "Remboursement effectué",
            $"Votre remboursement de {e.RefundAmount:0.00} {e.Currency} a été versé. " +
            "Selon votre opérateur, il peut apparaître sous 24 à 72 heures.",
            "Return",
            e.ReturnRequestId,
            cancellationToken);

        // LE VENDEUR ensuite : son solde vient d'être débité. L'apprendre par une
        // notification vaut mieux que de le découvrir en consultant son portefeuille
        // — et de croire à une erreur.
        try
        {
            var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
            if (seller is null)
            {
                _logger.LogError(
                    "Remboursement {ReturnRequestId} : vendeur {SellerId} introuvable — il ne sera pas informé du débit.",
                    e.ReturnRequestId, e.SellerId);
                return;
            }

            await _dispatcher.NotifyAsync(
                seller.UserId,
                "Retour remboursé au client",
                $"Un retour a été remboursé : {e.RefundAmount:0.00} {e.Currency}. " +
                "Le montant correspondant a été déduit de votre solde.",
                "Return",
                e.ReturnRequestId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le vendeur n'est pas joignable : ce n'est pas une raison pour faire
            // échouer le handler. L'acheteur, lui, a déjà été prévenu — et surtout,
            // la contre-passation comptable est déjà écrite. On ne rejoue rien.
            _logger.LogError(
                ex,
                "Remboursement {ReturnRequestId} : échec de la notification du vendeur {SellerId}.",
                e.ReturnRequestId, e.SellerId);
        }
    }
}
