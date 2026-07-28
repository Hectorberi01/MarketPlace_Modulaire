using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Prévient le vendeur qu'un reversement lui a été versé.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// L'ÉVÉNEMENT ÉTAIT PUBLIÉ DEPUIS TOUJOURS, SANS AUCUN CONSOMMATEUR.
///
/// Le vendeur était payé — et n'en savait rien. Il devait ouvrir son application et
/// comparer ses soldes pour deviner qu'un virement était parti. C'est précisément le
/// message qu'un vendeur attend le plus, et le seul qui n'arrivait jamais.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class PayoutPaidNotificationHandler : IIntegrationEventHandler<PayoutPaidIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<PayoutPaidNotificationHandler> _logger;

    public PayoutPaidNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<PayoutPaidNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(PayoutPaidIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // Traduction SellerId → UserId : le jeton d'appareil est porté par le COMPTE,
        // pas par la boutique (voir SellerOrderNotificationHandler).
        var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogError(
                "Reversement {PayoutId} : vendeur {SellerId} introuvable — il ne sera PAS prévenu de son paiement.",
                e.PayoutId, e.SellerId);
            return;
        }

        await _dispatcher.NotifyAsync(
            seller.UserId,
            "Paiement reçu",
            $"Un reversement de {e.NetAmount:0.00} {e.Currency} vous a été versé.",
            "Payout",
            e.PayoutId,
            cancellationToken);
    }
}
