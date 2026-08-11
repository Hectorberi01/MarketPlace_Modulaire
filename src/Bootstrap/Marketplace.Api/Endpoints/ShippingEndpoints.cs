using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Shipping : suivi et transitions des expéditions. Les
/// expéditions sont créées automatiquement à la confirmation d'une commande (une
/// par vendeur / lieu d'expédition) ; ici le vendeur les fait avancer.
/// </summary>
public static class ShippingEndpoints
{
    /// <summary>Enregistre les routes des expéditions (vendeur / admin authentifié).</summary>
    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder app)
    {
        var shipments = app.MapGroup("/api/shipments").WithTags("Shipping").RequireAuthorization();

        shipments.MapGet("/{id:guid}", GetAsync).WithName("GetShipment");
        shipments.MapGet("/order/{orderId:guid}", ListByOrderAsync).WithName("ListShipmentsByOrder");
        shipments.MapGet("/seller/{sellerId:guid}", ListBySellerAsync).WithName("ListShipmentsBySeller");
        shipments.MapPost("/{id:guid}/prepare", PrepareAsync).WithName("PrepareShipment");
        shipments.MapPost("/{id:guid}/ready", ReadyAsync).WithName("ReadyShipmentForPickup");
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
    /// <summary>
    /// Colis emballé, prêt à enlever. C'est ce geste qui déclenche la recherche
    /// d'un livreur HBA Delivery.
    /// </summary>
    private static async Task<IResult> ReadyAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkShipmentReadyForPickupCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

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

    /// <summary>Marque l'expédition « livrée ».</summary>
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
