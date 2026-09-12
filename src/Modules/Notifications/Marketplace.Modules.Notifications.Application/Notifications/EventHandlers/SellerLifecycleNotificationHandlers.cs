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
/// ═════════════════════════════════════════════════════════════════════════════════
/// FERMETURE À L'INITIATIVE DU VENDEUR — ET NON SUSPENSION.
///
/// Ce handler annonçait « Boutique suspendue », par notification ET par e-mail, à un
/// vendeur qui venait de fermer sa boutique LUI-MÊME. Il recevait l'annonce d'une
/// sanction qu'il n'avait pas subie, et l'invitation à contacter le support pour en
/// connaître un motif qui n'existait pas — pendant que le vrai suspendu, faute
/// d'événement, ne recevait rien du tout.
///
/// Les deux cas ont désormais chacun leur événement et chacun leur message.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerClosedNotificationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerClosedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique fermée",
            "Votre boutique est fermée : vos produits ne sont plus en vente. "
            + "Vous pouvez demander sa réouverture à tout moment depuis votre compte.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur que son dossier KYB a été REFUSÉ, avec le motif.
///
/// Aucun handler n'existait : le vendeur découvrait « Rejected » sur son écran de
/// compte, sans savoir quoi corriger. Doublé par e-mail, parce que c'est le message
/// qui débloque son inscription — et parce que sans lui il re-téléversera la même
/// pièce.
/// </summary>
public sealed class SellerKybRejectedNotificationHandler : IIntegrationEventHandler<SellerKybRejectedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerKybRejectedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerKybRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Dossier de vérification refusé",
            $"Votre dossier n'a pas été validé. Motif : {e.Reason} "
            + "Téléversez une pièce corrigée depuis votre compte : le dossier repartira automatiquement en revue.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur que sa boutique a été SUSPENDUE, AVEC LE MOTIF. La suspension
/// retire ses produits de la vitrine : la découvrir par la chute de ses ventes serait
/// le pire cas, et la découvrir sans savoir pourquoi ne lui permet rien de corriger.
/// Doublée par e-mail — il n'ouvrira pas forcément l'application ce jour-là.
/// </summary>
public sealed class SellerSuspendedNotificationHandler : IIntegrationEventHandler<SellerSuspendedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerSuspendedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // Le motif est exigé par le validateur de la commande d'administration. Le
        // repli ne couvre donc qu'une suspension écrite par un autre chemin : mieux
        // vaut un renvoi vers le support qu'une phrase qui s'arrête au milieu.
        var motif = string.IsNullOrWhiteSpace(e.Reason)
            ? "Contactez le support pour en connaître le motif."
            : $"Motif : {e.Reason}";

        return _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique suspendue",
            $"Votre boutique a été suspendue : vos produits ne sont plus visibles. {motif}",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
    }
}

/// <summary>
/// Prévient le vendeur que la suspension est levée. Le message dit explicitement que
/// la remise en vitrine lui revient : Catalog ne republie pas de lui-même, et sans
/// cette phrase le vendeur attendrait un retour automatique qui n'arrivera pas.
/// </summary>
public sealed class SellerSuspensionLiftedNotificationHandler
    : IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerSuspensionLiftedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerSuspensionLiftedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Suspension levée",
            "Votre boutique est de nouveau active. Vos fiches produit ont été dépubliées pendant la suspension : "
            + "republiez celles que vous souhaitez remettre en vente.",
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
            // ═══════════════════════════════════════════════════════════════════════
            // CE TYPE DISAIT « Order » POUR UN IDENTIFIANT D'ARTICLE DE STOCK.
            //
            // `relatedType` sert à deux choses : classer la notification dans une
            // catégorie de préférences, et dire aux clients vers quoi pointer. La
            // seconde était trompeuse — une console qui suit le type ouvre une fiche
            // COMMANDE avec un `InventoryItemId`, donc une commande introuvable.
            //
            // La catégorie ne bouge pas : `NotificationCategories` range « inventory »
            // avec les commandes, comme avant. Seule la cible change, et elle devient
            // exacte.
            // ═══════════════════════════════════════════════════════════════════════
            "Inventory",
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
