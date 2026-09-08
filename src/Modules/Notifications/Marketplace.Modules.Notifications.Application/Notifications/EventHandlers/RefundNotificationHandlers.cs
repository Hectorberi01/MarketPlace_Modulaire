using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// L'argent est ARRIVÉ sur la cagnotte. On prévient l'acheteur… et le vendeur, qui
/// vient d'être débité.
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
            $"Votre remboursement de {e.RefundAmount:0.00} {e.Currency} est disponible sur votre cagnotte. " +
            "Utilisez-le sur une prochaine commande, ou demandez son versement sur votre Mobile Money.",
            "Return",
            e.ReturnRequestId,
            cancellationToken,
            alsoEmail: true);

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
