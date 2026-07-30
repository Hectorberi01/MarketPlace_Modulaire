using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Inventory.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Prévient l'ADMIN qu'une boutique vient d'être créée et attend sa validation KYB.
/// Sans cela, un dossier pouvait dormir des jours : rien ne signalait son arrivée.
/// </summary>
public sealed class SellerRegisteredAdminNotificationHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AdminNotificationTarget _admin;

    public SellerRegisteredAdminNotificationHandler(NotificationDispatcher dispatcher, AdminNotificationTarget admin)
    {
        _dispatcher = dispatcher;
        _admin = admin;
    }

    public async Task HandleAsync(SellerRegisteredIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _admin.ResolveAsync(cancellationToken) is not { } adminUserId)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            adminUserId,
            "Nouvelle boutique à valider",
            $"La boutique « {e.ShopName} » a été créée et attend la vérification de son dossier KYB.",
            "Seller",
            e.SellerId,
            cancellationToken);
    }
}

/// <summary>
/// Prévient le vendeur que sa boutique a été SUSPENDUE. Une suspension retire ses
/// produits du catalogue : la découvrir par la chute de ses ventes serait le pire cas.
/// Doublée par e-mail — il n'ouvrira pas forcément l'application ce jour-là.
/// </summary>
public sealed class SellerClosedNotificationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerClosedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique suspendue",
            "Votre boutique a été suspendue : vos produits ne sont plus visibles. Contactez le support pour en connaître le motif.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>Prévient le vendeur que sa boutique est rouverte.</summary>
public sealed class SellerReactivatedNotificationHandler : IIntegrationEventHandler<SellerReactivatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerReactivatedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerReactivatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique réactivée",
            "Votre boutique est de nouveau active : vos produits sont visibles et vous pouvez recevoir des commandes.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur qu'un avis vient d'être publié sur l'un de ses produits.
/// L'événement ne porte que le produit : on remonte au vendeur via le catalogue.
/// </summary>
public sealed class ReviewPublishedNotificationHandler : IIntegrationEventHandler<ReviewPublishedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ICatalogModuleApi _catalog;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<ReviewPublishedNotificationHandler> _logger;

    public ReviewPublishedNotificationHandler(
        NotificationDispatcher dispatcher,
        ICatalogModuleApi catalog,
        ISellerModuleApi sellers,
        ILogger<ReviewPublishedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _catalog = catalog;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(ReviewPublishedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var product = await _catalog.GetProductAsync(e.ProductId, cancellationToken);
        if (product is null)
        {
            _logger.LogWarning("Avis {ReviewId} : produit {ProductId} introuvable — vendeur non prévenu.", e.ReviewId, e.ProductId);
            return;
        }

        // SellerId → UserId : le jeton d'appareil est porté par le compte.
        var seller = await _sellers.GetSellerAsync(product.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogWarning("Avis {ReviewId} : vendeur {SellerId} introuvable — non prévenu.", e.ReviewId, product.SellerId);
            return;
        }

        var stars = new string('★', Math.Clamp(e.Rating, 0, 5));

        await _dispatcher.NotifyAsync(
            seller.UserId,
            "Nouvel avis client",
            $"« {product.Name} » a reçu un avis {stars} ({e.Rating}/5).",
            "Review",
            e.ProductId,
            cancellationToken);
    }
}

/// <summary>
/// Prévient le vendeur qu'une de ses références est en RUPTURE. Un produit en rupture
/// ne se vend plus : chaque heure sans le savoir est une vente perdue.
/// </summary>
public sealed class StockDepletedNotificationHandler : IIntegrationEventHandler<StockDepletedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOfferModuleApi _offers;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<StockDepletedNotificationHandler> _logger;

    public StockDepletedNotificationHandler(
        NotificationDispatcher dispatcher,
        IOfferModuleApi offers,
        ISellerModuleApi sellers,
        ILogger<StockDepletedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _offers = offers;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(StockDepletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'inventaire raisonne en SKU ; c'est l'offre qui connaît le vendeur.
        var offer = (await _offers.GetOffersBySkuAsync(e.Sku, cancellationToken)).FirstOrDefault();
        if (offer is null)
        {
            _logger.LogWarning("Rupture {Sku} : aucune offre correspondante — vendeur non prévenu.", e.Sku);
            return;
        }

        var seller = await _sellers.GetSellerAsync(offer.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogWarning("Rupture {Sku} : vendeur {SellerId} introuvable — non prévenu.", e.Sku, offer.SellerId);
            return;
        }

        await _dispatcher.NotifyAsync(
            seller.UserId,
            "Rupture de stock",
            $"La référence {e.Sku} n'a plus de stock : elle n'est plus proposée à la vente. Réapprovisionnez-la pour la remettre en ligne.",
            "Order",
            e.InventoryItemId,
            cancellationToken);
    }
}

/// <summary>
/// Prévient l'acheteur que son PAIEMENT A ÉCHOUÉ. Sans ce message, il croit sa commande
/// passée et attend un colis qui ne partira jamais.
/// </summary>
public sealed class PaymentFailedNotificationHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orders;
    private readonly ILogger<PaymentFailedNotificationHandler> _logger;

    public PaymentFailedNotificationHandler(
        NotificationDispatcher dispatcher, IOrderingModuleApi orders, ILogger<PaymentFailedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentFailedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Paiement {PaymentId} échoué : commande {OrderId} introuvable.", e.PaymentId, e.OrderId);
            return;
        }

        // Le MOTIF technique du PSP n'est pas repris : il est rarement compréhensible et
        // parfois révélateur (numéro, solde). On invite à réessayer.
        await _dispatcher.NotifyAsync(
            order.BuyerId,
            "Paiement échoué",
            "Votre paiement n'a pas abouti et votre commande n'a pas été validée. Vous pouvez réessayer depuis votre panier.",
            "Payment",
            e.OrderId,
            cancellationToken,
            alsoEmail: true);
    }
}
