using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Application.Carriers;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — exécution. La file est la liste d'expéditions de la boutique du
/// jeton ; chaque accès/mutation par id vérifie l'appartenance (anti-IDOR).
/// </summary>
public static class SellerFulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapSellerFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        var ship = app.MapSellerGroup("/seller/shipments", "Vendeur · Exécution");

        ship.MapGet("/", QueueAsync);
        ship.MapGet("/by-order/{orderId:guid}", ByOrderAsync);
        ship.MapGet("/{id:guid}", GetAsync);
        ship.MapPost("/{id:guid}/prepare", PrepareAsync);
        ship.MapPost("/{id:guid}/ship", ShipAsync);
        ship.MapPost("/{id:guid}/deliver", DeliverAsync);
        ship.MapPost("/{id:guid}/cancel", CancelAsync);

        // Catalogue des transporteurs actifs (le vendeur choisit à l'expédition).
        var carriers = app.MapSellerGroup("/seller/carriers", "Vendeur · Transporteurs");
        carriers.MapGet("/", ListActiveCarriersAsync);

        return app;
    }

    /// <summary>Transporteurs actifs proposés au vendeur (référentiel plateforme, lecture seule).</summary>
    private static async Task<IResult> ListActiveCarriersAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListActiveCarriersQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>
    /// File d'exécution : toutes les expéditions de la boutique du jeton, enrichies
    /// pour le dashboard (référence commande, nom du client, nombre d'articles).
    /// </summary>
    private static async Task<IResult> QueueAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListShipmentsBySellerQuery(seller.Id), ct);
        return result.IsFailure
            ? result.Match(s => Results.Ok(s))
            : Results.Ok(await EnrichAsync(result.Value, identity, ct));
    }

    /// <summary>Expéditions d'une commande, filtrées sur la boutique du jeton (enrichies).</summary>
    private static async Task<IResult> ByOrderAsync(ClaimsPrincipal user, Guid orderId, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListShipmentsByOrderQuery(orderId), ct);
        return result.IsFailure
            ? result.Match(s => Results.Ok(s))
            : Results.Ok(await EnrichAsync(result.Value.Where(s => s.SellerId == seller.Id), identity, ct));
    }

    /// <summary>
    /// Projette des expéditions vers la forme attendue par le dashboard : référence
    /// commande lisible, nom du client (résolu via Identity), nombre d'articles, et
    /// statut renommé (« Preparing » côté domaine → « ReadyForPickup » ici, voir
    /// <see cref="DashboardStatus"/>).
    /// </summary>
    private static async Task<List<object>> EnrichAsync(IEnumerable<ShipmentSummary> shipments, IIdentityModuleApi identity, CancellationToken ct)
    {
        var rows = new List<object>();
        foreach (var s in shipments)
        {
            var customer = "Client";
            var u = await identity.GetUserAsync(s.BuyerId, ct);
            if (u is not null)
            {
                var name = $"{u.FirstName} {u.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    customer = name;
                }
            }

            rows.Add(new
            {
                id = s.Id,
                orderId = s.OrderId,
                orderReference = OrderReference(s.OrderId),
                customer,
                status = DashboardStatus(s.Status),
                carrier = s.Carrier,
                trackingNumber = s.TrackingNumber,
                trackingUrl = s.TrackingUrl,
                // Sans lui, un colis porté par un coursier de la plateforme s'affiche
                // avec un transporteur vide et un suivi vide — indiscernable d'un colis
                // que personne n'a encore pris. Voir ShipmentDeliveryMode.
                deliveryMode = s.DeliveryMode,
                // ─────────────────────────────────────────────────────────────────
                // LE MOTIF D'UN COURSIER QUI A RENONCÉ, SANS QUOI LA NOTIFICATION
                // ENVOYÉE AU VENDEUR NE SE VÉRIFIE NULLE PART.
                //
                // `MarkCourierDeliveryFailed` n'AVANCE PAS l'état : l'expédition reste
                // « en préparation », rigoureusement identique à celle que le vendeur
                // vient de préparer. Le champ est donc la SEULE chose qui distingue
                // « personne ne l'a encore prise » de « quelqu'un a essayé et a
                // renoncé » — et c'est la seconde qui exige un geste de sa part.
                //
                // Nul sur l'immense majorité des expéditions, et c'est normal.
                // ─────────────────────────────────────────────────────────────────
                courierFailureReason = s.CourierFailureReason,
                itemCount = s.Items.Sum(i => i.Quantity),
                createdAt = s.CreatedAtUtc,
            });
        }

        return rows;
    }

    private static string OrderReference(Guid orderId)
    {
        var hex = orderId.ToString("N").ToUpperInvariant();
        return $"CMD-{hex[..8]}";
    }

    /// <summary>
    /// Le tableau de bord nomme « ReadyForPickup » ce que le domaine appelle « Preparing ».
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// L'ANCIENNE ÉTIQUETTE ÉTAIT « Prepared », ET ELLE MENTAIT SUR LE SENS DE L'ÉTAPE.
    ///
    /// Un participe passé décrit une tâche ACHEVÉE : le vendeur lisait « Préparée » et
    /// comprenait qu'il n'y avait plus rien à attendre. Or c'est exactement l'inverse —
    /// c'est le moment où la course est proposée aux coursiers, et donc l'état dans
    /// lequel un colis ATTEND d'être ramassé.
    ///
    /// Cette étiquette a sa part dans le défaut corrigé côté domaine : elle faisait
    /// lire la préparation comme une écriture comptable facultative, alors qu'elle est
    /// le seul déclencheur de la livraison. Marquer préparé et ne rien voir venir se
    /// comprenait comme une fin de parcours plutôt que comme une attente.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static string DashboardStatus(string backendStatus)
        => string.Equals(backendStatus, "Preparing", StringComparison.OrdinalIgnoreCase)
            ? "ReadyForPickup"
            : backendStatus;

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return await SellerBff.OwnsShipmentAsync(sender, seller.Id, id, ct)
            ? (await sender.Send(new GetShipmentQuery(id), ct)).Match(s => Results.Ok(s))
            : SellerBff.NotFoundOrForbidden("shipment");
    }

    private static async Task<IResult> PrepareAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkShipmentPreparingCommand(id), ct));

    private static async Task<IResult> ShipAsync(ClaimsPrincipal user, Guid id, ShipRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new MarkShipmentShippedCommand(id, request.Carrier, request.TrackingNumber, request.CarrierId), ct));

    private static async Task<IResult> DeliverAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkShipmentDeliveredCommand(id), ct));

    private static async Task<IResult> CancelAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new CancelShipmentCommand(id), ct));

    /// <summary>
    /// Résout le vendeur, vérifie la propriété de l'expédition, puis exécute la commande.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// AUCUN CONTRÔLE DE STATUT ICI, ET C'EST UNE DÉCISION, PAS UN OUBLI.
    ///
    /// Les autres surfaces d'écriture du BFF passent désormais par
    /// `ResolveSellingSellerAsync` (voir SellerRights). Celle-ci ne le fait pas :
    /// préparer, expédier, livrer ou annuler porte sur une commande DÉJÀ PAYÉE.
    ///
    /// Bloquer ces transitions ne sanctionnerait pas le vendeur — il a encaissé — mais
    /// l'ACHETEUR, qui attendrait indéfiniment un colis que plus personne n'a le droit
    /// de faire partir. La suspension d'une boutique arrête ses ventes FUTURES ; elle
    /// ne peut pas prendre en otage celles qui sont conclues.
    ///
    /// Le levier existe déjà et il est au bon endroit : la suspension coupe la
    /// publication en amont, et le versement des fonds en aval
    /// (SellerRights.CanWithdraw). Entre les deux, l'obligation de livrer reste due.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid shipmentId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsShipmentAsync(sender, seller.Id, shipmentId, ct))
        {
            return SellerBff.NotFoundOrForbidden("shipment");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <summary>
    /// [CarrierId] : transporteur du catalogue (recommandé — produit le lien de suivi).
    /// [Carrier] : nom libre, repli si le catalogue n'est pas utilisé.
    /// </summary>
    public sealed record ShipRequest(string Carrier, string TrackingNumber, Guid? CarrierId = null);
}
