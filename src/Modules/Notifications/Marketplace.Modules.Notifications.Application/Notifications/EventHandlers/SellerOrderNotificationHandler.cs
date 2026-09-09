using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Prévient CHAQUE VENDEUR concerné qu'une commande payée l'attend.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE HANDLER N'EXISTAIT PAS. C'ÉTAIT LE TROU.
///
/// Toute l'infrastructure de push était en place — jetons d'appareil, FcmPushSender,
/// purge des jetons morts, et le BFF vendeur exposait déjà l'enregistrement du jeton.
/// Mais les trois handlers de commande notifiaient tous `e.BuyerId`. L'acheteur, et
/// seulement lui. Un vendeur pouvait vendre sans jamais l'apprendre.
///
/// Et l'événement lui-même ne permettait pas de le corriger : `OrderConfirmedIntegrationEvent`
/// ne transportait que l'acheteur et l'identifiant de commande. Il fallait donc
/// commencer par lui faire porter la répartition par vendeur — sans quoi ce handler
/// n'aurait eu personne à prévenir.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// <para>
/// POURQUOI À LA CONFIRMATION, ET NON À LA COMMANDE.
///
/// Une commande « passée » n'est encore qu'une intention : le paiement FedaPay peut
/// être abandonné. Prévenir les vendeurs à ce moment-là, ce serait les faire préparer
/// — voire expédier — une marchandise qui ne sera jamais payée, et les noyer sous des
/// alertes sans suite. Ils cesseraient de les lire, et manqueraient les vraies.
///
/// La confirmation signifie « l'argent est encaissé ». C'est le seul moment où
/// l'alerte est actionnable.
/// </para>
///
/// <para>
/// ─────────────────────────────────────────────────────────────────────────────
/// UNE SEULE ALERTE PAR VENDEUR ET PAR COMMANDE, MÊME SI L'ÉVÉNEMENT EST REJOUÉ.
///
/// Ce handler n'avait aucune garde, alors que six des huit consommateurs
/// d'`OrderConfirmedIntegrationEvent` en ont une. Le dispatch est « au moins une fois »
/// et Notifications passe AVANT Settlement, Loyalty et Analytics (`ModuleRegistry`) :
/// une panne dans l'un de ces trois rejouait le message entier et repassait ici, dix
/// fois au maximum. Chaque vendeur recevait donc jusqu'à dix « Nouvelle commande à
/// préparer » pour une seule vente — et un vendeur noyé sous les doublons est un
/// vendeur qui cesse de lire ses alertes, ce qui annule le bénéfice même de ce handler.
///
/// La clé inclut le VENDEUR : une commande multi-vendeurs doit produire une alerte par
/// vendeur. Dédupliquer sur la seule commande ferait taire tous les vendeurs sauf le
/// premier — un défaut pire que celui corrigé.
/// ─────────────────────────────────────────────────────────────────────────────
/// </para>
public sealed class SellerOrderConfirmedNotificationHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<SellerOrderConfirmedNotificationHandler> _logger;

    public SellerOrderConfirmedNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<SellerOrderConfirmedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.SellerShares.Count == 0)
        {
            // Ne devrait pas arriver : une commande sans ligne n'existe pas. Si cela
            // se produit, c'est un bug en amont — et le silence serait le pire des
            // symptômes, puisque le vendeur, lui, ne saurait jamais qu'on l'a oublié.
            _logger.LogWarning(
                "Commande {OrderId} confirmée sans aucun vendeur : aucune notification vendeur envoyée.",
                e.OrderId);
            return;
        }

        foreach (var share in e.SellerShares)
        {
            try
            {
                // TRADUCTION SellerId → UserId.
                //
                // Une commande connaît des VENDEURS (SellerId). Un push s'adresse à un
                // COMPTE (UserId) : c'est sur le compte que le jeton d'appareil est
                // enregistré, par /seller/notifications/devices. Sans cette étape, on
                // notifierait un identifiant qui ne possède aucun appareil, et le push
                // partirait dans le vide — sans la moindre erreur.
                //
                // L'appel est quasi gratuit : les vendeurs sont en cache (SellersCacheKeys).
                var seller = await _sellers.GetSellerAsync(share.SellerId, cancellationToken);

                if (seller is null)
                {
                    _logger.LogError(
                        "Commande {OrderId} : vendeur {SellerId} introuvable — il ne sera PAS prévenu de sa vente.",
                        e.OrderId, share.SellerId);
                    continue;
                }

                var articles = share.ItemCount == 1 ? "1 article" : $"{share.ItemCount} articles";

                await _dispatcher.NotifyOnceAsync(
                    NotificationDeduplicationKeys.OrderConfirmedSeller(e.OrderId, seller.UserId),
                    seller.UserId,
                    "Nouvelle commande à préparer",
                    $"Paiement reçu : {articles} pour {share.Amount:0.00} {e.Currency}. Préparez la commande.",
                    "Order",
                    e.OrderId,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un vendeur injoignable ne doit pas priver les AUTRES de leur
                // notification. Sur une commande multi-vendeurs, laisser l'exception
                // remonter ferait perdre toutes les alertes suivantes — la boucle
                // s'arrêterait au premier échec.
                _logger.LogError(
                    ex,
                    "Commande {OrderId} : échec de la notification du vendeur {SellerId}. Les autres vendeurs sont tout de même prévenus.",
                    e.OrderId, share.SellerId);
            }
        }
    }
}
