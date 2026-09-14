using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Disputes.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// QUELS VENDEURS PRÉVENIR D'UN LITIGE — LES EXPÉDITEURS, PAS LES VENDEURS DE LIGNES.
///
/// Le module Litiges ne connaît ni vendeur ni boutique : un litige est rattaché à une
/// COMMANDE et à l'acheteur qui l'a ouvert. `DisputeOpenedIntegrationEvent` ne porte
/// donc qu'un `OrderId`, et il faut redescendre pour trouver qui prévenir.
///
/// LE CRITÈRE EST CELUI DU CONTRÔLE D'ACCÈS, ET C'EST TOUT L'ENJEU.
/// `SellerBff.IsInvolvedInOrderAsync` et `GET /seller/disputes` partent l'un et l'autre
/// des EXPÉDITIONS de la commande, jamais de ses lignes. On reprend exactement cette
/// requête.
///
/// Une première rédaction partait des LIGNES de commande — plus large, donc « plus
/// généreux ». C'était un piège : un litige « Colis non reçu » est ouvert précisément
/// quand rien n'est parti, donc quand AUCUNE expédition n'existe. Le vendeur recevait
/// alors « Répondez depuis l'écran Litiges : votre version compte dans la décision »,
/// tapait sur la notification, et tombait sur une liste vide — le BFF ne lui montre
/// que les litiges des commandes qu'il a expédiées, et le détail lui répondrait 403.
/// On lui réclamait une réponse en l'envoyant sur une porte fermée.
///
/// CE QUE CE CHOIX LAISSE OUVERT, ET QU'IL FAUT SAVOIR : un litige ouvert AVANT toute
/// expédition ne prévient aujourd'hui personne côté vendeur. Ce n'est pas un oubli
/// d'ici — c'est la conséquence du périmètre choisi par le BFF, qui le documente
/// lui-même (« un litige ouvert avant la moindre expédition n'apparaît pas »). Le
/// corriger suppose d'élargir le contrôle d'accès du BFF aux lignes de commande, des
/// deux côtés à la fois. Tant que ce n'est pas fait, mieux vaut ne rien envoyer
/// qu'envoyer vers un écran vide.
///
/// TRADUCTION SellerId → UserId, obligatoire : le jeton d'appareil est enregistré sur
/// le COMPTE, jamais sur la boutique. Notifier un `SellerId` enverrait le push dans le
/// vide, sans la moindre erreur.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DisputeSellerTargets
{
    private readonly IShippingModuleApi _shipping;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<DisputeSellerTargets> _logger;

    public DisputeSellerTargets(
        IShippingModuleApi shipping, ISellerModuleApi sellers, ILogger<DisputeSellerTargets> logger)
    {
        _shipping = shipping;
        _sellers = sellers;
        _logger = logger;
    }

    /// <summary>Comptes des vendeurs ayant expédié sur cette commande. Vide si aucun.</summary>
    public async Task<IReadOnlyList<Guid>> ResolveAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var shipments = await _shipping.GetShipmentsByOrderAsync(orderId, cancellationToken);
        if (shipments.Count == 0)
        {
            _logger.LogInformation(
                "Litige sur la commande {OrderId} : aucune expédition — aucun vendeur n'est prévenu "
                + "(il ne pourrait pas ouvrir le litige depuis sa console).",
                orderId);
            return Array.Empty<Guid>();
        }

        var comptes = new List<Guid>();
        foreach (var sellerId in shipments.Select(s => s.SellerId).Distinct())
        {
            var seller = await _sellers.GetSellerAsync(sellerId, cancellationToken);
            if (seller is null)
            {
                // On continue : un vendeur introuvable ne doit pas priver les autres de
                // leur notification. L'erreur est tracée, elle, parce qu'un vendeur
                // silencieusement oublié sur un litige est un vendeur qui perd son délai.
                _logger.LogError(
                    "Litige sur la commande {OrderId} : vendeur {SellerId} introuvable — il ne sera PAS prévenu.",
                    orderId, sellerId);
                continue;
            }

            comptes.Add(seller.UserId);
        }

        return comptes;
    }
}

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
/// Prévient l'ADMINISTRATEUR et LE OU LES VENDEURS qu'un litige vient d'être ouvert.
///
/// Sans cela, un litige n'existait que dans la console : il fallait aller le chercher.
/// Un acheteur en litige attend une réaction — le délai de première réponse est ce qui
/// distingue un incident réglé d'un client perdu.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LE VENDEUR N'ÉTAIT PRÉVENU DE RIEN. C'ÉTAIT LE TROU.
///
/// Ce handler ne notifiait que l'administrateur, et celui de la décision ne notifiait
/// que l'acheteur. Le vendeur, lui, n'apprenait un litige qu'en ouvrant l'écran
/// « Litiges » par hasard — alors que toute la plomberie l'attendait : la console a cet
/// écran, elle mappe déjà `dispute` vers lui, et `NotificationCategories` range
/// `dispute` dans « Retours ». Il ne manquait que l'émetteur.
///
/// Sur la fonction où le délai de réponse joue le plus contre lui.
///
/// L'ADMIN ET LE VENDEUR SONT INDÉPENDANTS. Une première rédaction sortait tôt quand
/// l'e-mail d'administration n'était pas configuré : le vendeur payait alors une
/// erreur de configuration qui ne le concerne pas. Les deux envois ne se conditionnent
/// plus l'un l'autre.
///
/// PUSH SEUL, PAS D'E-MAIL. Un litige ouvert est une tâche : elle vit dans la console,
/// où le vendeur doit répondre. La DÉCISION, elle, part aussi par e-mail — c'est une
/// issue qu'il doit connaître même sans ouvrir l'application.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class DisputeOpenedNotificationHandler : IIntegrationEventHandler<DisputeOpenedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AdminNotificationTarget _admin;
    private readonly DisputeSellerTargets _vendeurs;
    private readonly ILogger<DisputeOpenedNotificationHandler> _logger;

    public DisputeOpenedNotificationHandler(
        NotificationDispatcher dispatcher,
        AdminNotificationTarget admin,
        DisputeSellerTargets vendeurs,
        ILogger<DisputeOpenedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _admin = admin;
        _vendeurs = vendeurs;
        _logger = logger;
    }

    public async Task HandleAsync(DisputeOpenedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════════════
        // `NotifyOnceAsync` ET UN `catch` PAR DESTINATAIRE — LES DEUX, PAS L'UN DES DEUX.
        //
        // L'outbox livre AU MOINS UNE FOIS et rejoue le message ENTIER, jusqu'à dix
        // fois, dès qu'un handler quelconque échoue. Sans clé d'unicité, un incident
        // ailleurs dans la chaîne renotifiait l'admin et les vendeurs déjà servis à
        // chaque tentative. Sans `catch` par itération, un seul vendeur injoignable
        // arrêtait la boucle et privait TOUS les suivants — puis la rejouait en entier.
        //
        // C'est exactement le couple que `SellerOrderNotificationHandler` applique déjà
        // sur sa boucle de vendeurs. Un litige ouvert est un fait unique : il a sa clé.
        // ═════════════════════════════════════════════════════════════════════════════
        try
        {
            if (await _admin.ResolveAsync(cancellationToken) is { } adminUserId)
            {
                await _dispatcher.NotifyOnceAsync(
                    NotificationDeduplicationKeys.DisputeOpenedAdmin(e.DisputeId),
                    adminUserId,
                    "Nouveau litige à traiter",
                    $"Un litige « {Label(e.Type)} » vient d'être ouvert sur une commande.",
                    "Dispute",
                    e.DisputeId,
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un annuaire d'identité qui hoquette ne doit pas priver les VENDEURS de
            // leur alerte : eux ont un délai de réponse, l'admin a une console.
            _logger.LogError(ex, "Litige {DisputeId} ouvert : l'administrateur n'a PAS été prévenu.", e.DisputeId);
        }

        foreach (var compte in await _vendeurs.ResolveAsync(e.OrderId, cancellationToken))
        {
            try
            {
                await _dispatcher.NotifyOnceAsync(
                    NotificationDeduplicationKeys.DisputeOpenedSeller(e.DisputeId, compte),
                    compte,
                    "Litige ouvert sur une commande",
                    $"Un acheteur a ouvert un litige « {Label(e.Type)} ». "
                    + "Répondez depuis l'écran Litiges : votre version compte dans la décision.",
                    "Dispute",
                    e.DisputeId,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex, "Litige {DisputeId} ouvert : le vendeur {Compte} n'a PAS été prévenu.", e.DisputeId, compte);
            }
        }
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
/// Prévient l'ACHETEUR et LE OU LES VENDEURS de la décision rendue sur un litige.
///
/// Pour l'acheteur, c'est l'issue qu'il attend ; la lui laisser découvrir en rouvrant
/// l'application serait le pire moment de la relation. Pour le vendeur, c'est la fin
/// d'un dossier ouvert contre lui, et — selon la décision — une somme qui ne lui
/// reviendra pas. Le laisser l'apprendre par son solde serait pire encore.
///
/// DEUX TEXTES, PAS UN SEUL RECOPIÉ. « Votre litige est tranché en votre faveur » n'a
/// aucun sens envoyé au vendeur : la faveur est celle de l'autre partie. Chaque camp a
/// sa formulation, et chacune dit la décision DU POINT DE VUE de son destinataire.
/// </summary>
public sealed class DisputeResolvedNotificationHandler : IIntegrationEventHandler<DisputeResolvedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orders;
    private readonly DisputeSellerTargets _vendeurs;
    private readonly ILogger<DisputeResolvedNotificationHandler> _logger;

    public DisputeResolvedNotificationHandler(
        NotificationDispatcher dispatcher,
        IOrderingModuleApi orders,
        DisputeSellerTargets vendeurs,
        ILogger<DisputeResolvedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _orders = orders;
        _vendeurs = vendeurs;
        _logger = logger;
    }

    public async Task HandleAsync(DisputeResolvedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'événement ne porte pas l'acheteur : on le retrouve via la commande.
        var order = await _orders.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogError(
                "Litige {DisputeId} tranché : commande {OrderId} introuvable — NI l'acheteur NI les vendeurs ne sont prévenus.",
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

        // ═════════════════════════════════════════════════════════════════════════════
        // ET MAINTENANT LE VENDEUR. TROIS RÈGLES, CHACUNE APPRISE À SES DÉPENS.
        //
        // 1. ON N'ANNONCE AUCUN MOUVEMENT D'ARGENT. La règle posée plus haut vaut des
        //    deux côtés : LE LITIGE ARBITRE, IL NE PAIE PAS. Une première rédaction
        //    disait « le montant vous sera repris lors de votre prochain versement » —
        //    exactement la promesse vide que le message acheteur a dû abandonner, et
        //    pour la même raison : `DisputeResolvedIntegrationEvent` n'a toujours AUCUN
        //    consommateur dans Settlement. Le seul circuit de reprise existant part
        //    d'un RETOUR confirmé (`ReturnRefundedIntegrationEvent`), qui n'existe pas
        //    forcément ici — un « colis non reçu » n'a rien à retourner.
        //
        // 2. ON NE CITE PAS LE MONTANT. Une commande peut avoir été expédiée par
        //    plusieurs boutiques, et le litige, lui, n'est rattaché qu'à la COMMANDE :
        //    l'événement ne porte ni ligne ni vendeur. Écrire « remboursement partiel
        //    de 15 000 » l'enverrait donc aussi à un vendeur étranger à l'affaire — qui
        //    se croirait débité de cette somme, et apprendrait au passage le montant du
        //    dossier d'un concurrent. Le détail est dans l'écran Litiges, où le contrôle
        //    d'accès s'applique.
        //
        // 3. PAS D'E-MAIL. `alsoEmail` contourne les préférences de notification —
        //    seul le push les consulte. Un vendeur qui a coupé « Retours et litiges »
        //    recevrait quand même l'e-mail : un réglage qui ne règle qu'à moitié, sur la
        //    catégorie qu'on coupe précisément parce qu'on la juge bruyante.
        // ═════════════════════════════════════════════════════════════════════════════
        var bodyVendeur = e.Resolution.ToLowerInvariant() switch
        {
            "refundbuyer" =>
                "Le litige sur une de vos commandes est tranché en faveur de l'acheteur : un remboursement "
                + "lui est accordé. Ouvrez l'écran Litiges pour le détail ; notre service client revient "
                + "vers vous pour les suites.",
            "partialrefund" =>
                "Le litige sur une de vos commandes est tranché : un remboursement partiel est accordé à "
                + "l'acheteur. Ouvrez l'écran Litiges pour le détail ; notre service client revient vers "
                + "vous pour les suites.",
            "releasetoseller" =>
                "Le litige sur une de vos commandes est tranché en votre faveur : la vente est maintenue.",
            _ => "Le litige sur une de vos commandes a été tranché. Consultez le détail dans l'écran Litiges.",
        };

        foreach (var compte in await _vendeurs.ResolveAsync(e.OrderId, cancellationToken))
        {
            try
            {
                await _dispatcher.NotifyOnceAsync(
                    NotificationDeduplicationKeys.DisputeResolvedSeller(e.DisputeId, compte),
                    compte,
                    "Litige tranché",
                    bodyVendeur,
                    "Dispute",
                    e.DisputeId,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un échec ici ne doit pas rejouer le message : l'acheteur a DÉJÀ été
                // notifié et emailé quelques lignes plus haut, et un rejeu le lui
                // renverrait jusqu'à dix fois. On trace, et on continue.
                _logger.LogError(
                    ex, "Litige {DisputeId} tranché : le vendeur {Compte} n'a PAS été prévenu.", e.DisputeId, compte);
            }
        }
    }
}
