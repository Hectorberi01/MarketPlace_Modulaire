using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Disputes.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Ordering.Contracts;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Résout le compte administrateur à prévenir, à partir de l'e-mail d'exploitation
/// (`Identity:Bootstrap:AdminEmail`, déjà présent dans la configuration des conteneurs).
///
/// Il n'existe pas de « liste des admins » exposée entre modules ; s'appuyer sur cet
/// e-mail évite d'ouvrir l'annuaire d'Identity pour un seul besoin. Si la clé n'est pas
/// renseignée, on trace et on n'envoie rien — surtout pas d'exception : un litige doit
/// s'ouvrir même si personne n'est prévenu.
/// </summary>
public sealed class AdminNotificationTarget
{
    private readonly IIdentityModuleApi _identity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminNotificationTarget> _logger;

    public AdminNotificationTarget(
        IIdentityModuleApi identity, IConfiguration configuration, ILogger<AdminNotificationTarget> logger)
    {
        _identity = identity;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Identifiant du compte admin à notifier, ou null s'il est introuvable.</summary>
    public async Task<Guid?> ResolveAsync(CancellationToken cancellationToken)
    {
        var email = _configuration["Identity:Bootstrap:AdminEmail"];
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "Notification admin ignorée : « Identity:Bootstrap:AdminEmail » n'est pas renseigné.");
            return null;
        }

        var user = await _identity.GetUserByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning(
                "Notification admin ignorée : aucun compte pour l'e-mail d'administration configuré.");
            return null;
        }

        return user.Id;
    }
}

/// <summary>
/// Prévient l'ADMINISTRATEUR qu'un litige vient d'être ouvert.
///
/// Sans cela, un litige n'existait que dans la console : il fallait aller le chercher.
/// Un acheteur en litige attend une réaction — le délai de première réponse est ce qui
/// distingue un incident réglé d'un client perdu.
/// </summary>
public sealed class DisputeOpenedNotificationHandler : IIntegrationEventHandler<DisputeOpenedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AdminNotificationTarget _admin;

    public DisputeOpenedNotificationHandler(NotificationDispatcher dispatcher, AdminNotificationTarget admin)
    {
        _dispatcher = dispatcher;
        _admin = admin;
    }

    public async Task HandleAsync(DisputeOpenedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _admin.ResolveAsync(cancellationToken) is not { } adminUserId)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            adminUserId,
            "Nouveau litige à traiter",
            $"Un litige « {Label(e.Type)} » vient d'être ouvert sur une commande.",
            "Dispute",
            e.DisputeId,
            cancellationToken);
    }

    private static string Label(string type) => type.ToLowerInvariant() switch
    {
        "notreceived" => "Colis non reçu",
        "notconforming" => "Produit non conforme",
        "damageditem" => "Article endommagé",
        _ => "Autre problème",
    };
}

/// <summary>
/// Prévient l'ACHETEUR de la décision rendue sur son litige. C'est l'issue qu'il attend ;
/// la lui laisser découvrir en rouvrant l'application serait le pire moment de la relation.
/// </summary>
public sealed class DisputeResolvedNotificationHandler : IIntegrationEventHandler<DisputeResolvedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orders;
    private readonly ILogger<DisputeResolvedNotificationHandler> _logger;

    public DisputeResolvedNotificationHandler(
        NotificationDispatcher dispatcher, IOrderingModuleApi orders, ILogger<DisputeResolvedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(DisputeResolvedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'événement ne porte pas l'acheteur : on le retrouve via la commande.
        var order = await _orders.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogError(
                "Litige {DisputeId} tranché : commande {OrderId} introuvable — l'acheteur n'est PAS prévenu.",
                e.DisputeId, e.OrderId);
            return;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // CE MESSAGE PROMETTAIT UN VERSEMENT QUE PERSONNE N'EXÉCUTAIT.
        //
        // Il disait « vous serez remboursé » et « un remboursement de X vous sera
        // versé ». Or `DisputeResolvedIntegrationEvent` n'a QU'UN SEUL consommateur dans
        // tout le dépôt : celui-ci. Settlement n'en a aucun. Zéro franc ne bougeait, et
        // aucune tâche n'était créée nulle part.
        //
        // C'est pire qu'un trou d'argent : c'est un engagement écrit, envoyé par
        // notification ET par e-mail, sans contrepartie et sans trace pour le support.
        //
        // LE LITIGE ARBITRE, IL NE PAIE PAS — c'est le choix retenu. Le remboursement
        // reste une décision humaine, exécutée par l'un des deux circuits existants
        // (retour ou versement direct), tous deux sous le plafond commun de la commande.
        // Le message annonce donc la DÉCISION, et dit qu'on revient vers l'acheteur. Il
        // ne fixe ni montant ni délai, parce qu'aucun des deux n'est encore engagé.
        //
        // La contrepartie est en face : `GET /admin/disputes/refunds-outstanding` liste
        // ce qui a été accordé et ce qui a réellement été versé. Sans cette route, le
        // message ci-dessous serait devenu une promesse aussi vide que la précédente,
        // seulement moins précise.
        // ═════════════════════════════════════════════════════════════════════════
        var body = e.Resolution.ToLowerInvariant() switch
        {
            "refundbuyer" =>
                "Votre litige est tranché en votre faveur : un remboursement vous est accordé. "
                + "Notre service client revient vers vous pour les modalités.",
            "partialrefund" => e.RefundAmount is { } amount
                ? $"Votre litige est tranché : un remboursement partiel de {amount:0.00} vous est accordé. "
                  + "Notre service client revient vers vous pour les modalités."
                : "Votre litige est tranché : un remboursement partiel vous est accordé. "
                  + "Notre service client revient vers vous pour les modalités.",
            "releasetoseller" => "Votre litige est tranché : après examen, la vente est maintenue.",
            _ => "Votre litige a été tranché. Consultez le détail dans l'application.",
        };

        await _dispatcher.NotifyAsync(order.BuyerId, "Litige tranché", body, "Dispute", e.DisputeId, cancellationToken, alsoEmail: true);
    }
}
