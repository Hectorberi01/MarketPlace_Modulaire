using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Emails;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Boutique validée (vendeur activé par un administrateur) : on prévient le vendeur
/// par DEUX canaux — un e-mail de bienvenue ET une notification push + in-app. C'est
/// le moment où le vendeur passe de « en attente » à « peut vendre » : il doit le
/// savoir tout de suite, même app fermée (push).
/// </summary>
public sealed class SellerActivatedNotificationHandler : IIntegrationEventHandler<SellerActivatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IEmailSender _email;
    private readonly IIdentityModuleApi _identity;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<SellerActivatedNotificationHandler> _logger;

    public SellerActivatedNotificationHandler(
        NotificationDispatcher dispatcher,
        IEmailSender email,
        IIdentityModuleApi identity,
        ISellerModuleApi sellers,
        ILogger<SellerActivatedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _email = email;
        _identity = identity;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(SellerActivatedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
        var shopName = seller?.ShopName ?? "votre boutique";

        // 1) Push + notification in-app (best-effort côté push).
        await _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique validée 🎉",
            $"Votre boutique « {shopName} » est validée : vous pouvez désormais publier vos produits.",
            "Seller",
            e.SellerId,
            cancellationToken);

        // 2) E-mail de bienvenue. On résout l'adresse via le module Identity
        //    (l'événement ne porte que l'UserId).
        var user = await _identity.GetUserAsync(e.UserId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Vendeur {SellerId} activé : compte {UserId} introuvable ou sans e-mail — e-mail de validation NON envoyé.",
                e.SellerId, e.UserId);
            return;
        }

        var message = AccountEmailTemplates.SellerActivated(user.Email, user.FirstName, shopName);
        await _email.SendAsync(message, cancellationToken);
    }
}
