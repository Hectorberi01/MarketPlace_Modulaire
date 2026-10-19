using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Shipping : suivi et transitions des expéditions, RÉSERVÉS
/// AU BACK-OFFICE. Les expéditions sont créées automatiquement à la confirmation d'une
/// commande (une par vendeur / lieu d'expédition).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE GROUPE ÉTAIT OUVERT À TOUT COMPTE AUTHENTIFIÉ, ET C'ÉTAIT UNE VOIE D'ESCALADE.
///
/// `RequireAuthorization()` seul dit QUI parle, pas ce qu'il a le droit de toucher.
/// Or aucun de ces handlers ne vérifiait la propriété : ils prennent un identifiant
/// d'expédition et agissent dessus. N'importe quel compte — un acheteur inscrit ce
/// matin — pouvait donc appeler `/deliver` sur l'expédition de n'importe quel vendeur.
///
/// Ce n'était pas qu'un changement de statut. « Expédition livrée » déclenche en aval
/// la LIBÉRATION DU SÉQUESTRE : le gain du vendeur passe du solde à venir au solde
/// retirable, et devient versable sur son Mobile Money. Un tiers pouvait donc forcer le
/// paiement d'une commande jamais reçue, puis ouvrir un litige — la marchandise ET
/// l'argent. Les routes `/ship` et `/cancel` avaient le même défaut, et les lectures
/// exposaient les expéditions de tous les vendeurs.
///
/// Les vendeurs passent par le BFF Vendeur, qui exige le rôle ET vérifie la propriété
/// de l'expédition (SellerBff.OwnsShipmentAsync). L'API monolithe n'a aucune raison de
/// leur être ouverte — c'est très exactement ce que dit la règle posée dans
/// ApiAuthorization, et que ce groupe était le seul à enfreindre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ShippingEndpoints
{
    /// <summary>Enregistre les routes des expéditions (back-office uniquement).</summary>
    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // ADMIN SEUL. `MapAdminGroup` OUVRE AUSSI AUX MODÉRATEURS.
        //
        // `/{id}/deliver` fait lever `ShipmentDeliveredDomainEvent`, donc
        // `ReleaseSellerEarningsOnShipmentDeliveredHandler` : LA SÉQUESTRE DU VENDEUR EST
        // LIBÉRÉE. C'est un mouvement d'argent, pas un changement de statut.
        //
        // Le contraste rendait l'oubli visible : `POST /api/orders/{id}/deliver`, l'AUTRE
        // chemin vers la même libération, portait déjà `RequireRole("Admin")`. Cette
        // route-ci était son jumeau non gardé.
        // ═════════════════════════════════════════════════════════════════════════
        var shipments = app.MapAdminGroup("/api/shipments").WithTags("Shipping")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        shipments.MapGet("/{id:guid}", GetAsync).WithName("GetShipment");
        shipments.MapGet("/order/{orderId:guid}", ListByOrderAsync).WithName("ListShipmentsByOrder");
        shipments.MapGet("/seller/{sellerId:guid}", ListBySellerAsync).WithName("ListShipmentsBySeller");
        shipments.MapPost("/{id:guid}/prepare", PrepareAsync).WithName("PrepareShipment");
        shipments.MapPost("/{id:guid}/ship", ShipAsync).WithName("ShipShipment");
        shipments.MapPost("/{id:guid}/deliver", DeliverAsync).WithName("DeliverShipment");
        shipments.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelShipment");

        return app;
    }

    /// <summary>Récupère une expédition par son identifiant.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetShipmentQuery(id), ct);
        return result.Match(shipment => Results.Ok(shipment));
    }

    /// <summary>Liste les expéditions d'une commande.</summary>
    private static async Task<IResult> ListByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListShipmentsByOrderQuery(orderId), ct);
        return result.Match(shipments => Results.Ok(shipments));
    }

    /// <summary>Liste les expéditions d'un vendeur.</summary>
    private static async Task<IResult> ListBySellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListShipmentsBySellerQuery(sellerId), ct);
        return result.Match(shipments => Results.Ok(shipments));
    }

    /// <summary>Marque l'expédition « en préparation ».</summary>
    private static async Task<IResult> PrepareAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkShipmentPreparingCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque l'expédition « expédiée » (transporteur + suivi).</summary>
    private static async Task<IResult> ShipAsync(Guid id, ShipRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkShipmentShippedCommand(id, request.Carrier, request.TrackingNumber), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Marque l'expédition « livrée ».
    ///
    /// LIBÈRE LE SÉQUESTRE DU VENDEUR en aval : son gain devient retirable. C'est la
    /// route la plus lourde de conséquences du groupe, et la raison pour laquelle il est
    /// désormais réservé au back-office.
    /// </summary>
    private static async Task<IResult> DeliverAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkShipmentDeliveredCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Annule l'expédition.</summary>
    private static async Task<IResult> CancelAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CancelShipmentCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    public sealed record ShipRequest(string Carrier, string TrackingNumber);
}
