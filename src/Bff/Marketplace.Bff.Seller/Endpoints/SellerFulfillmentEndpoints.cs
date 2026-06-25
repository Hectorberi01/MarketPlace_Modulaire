using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — exécution des commandes. La file d'exécution du vendeur est sa
/// liste d'expéditions (scopée à la boutique du jeton) ; chaque expédition suit
/// le cycle préparation → expédiée (transporteur + suivi) → livrée.
/// </summary>
public static class SellerFulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapSellerFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        var ship = app.MapGroup("/seller/shipments").WithTags("Vendeur · Exécution").RequireAuthorization();

        ship.MapGet("/", QueueAsync);
        ship.MapGet("/by-order/{orderId:guid}", ByOrderAsync);
        ship.MapGet("/{id:guid}", GetAsync);
        ship.MapPost("/{id:guid}/prepare", PrepareAsync);
        ship.MapPost("/{id:guid}/ship", ShipAsync);
        ship.MapPost("/{id:guid}/deliver", DeliverAsync);
        ship.MapPost("/{id:guid}/cancel", CancelAsync);

        return app;
    }

    /// <summary>File d'exécution : toutes les expéditions de la boutique du jeton.</summary>
    private static async Task<IResult> QueueAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListShipmentsBySellerQuery(seller.Id), ct)).Match(s => Results.Ok(s));
    }

    /// <summary>Expéditions rattachées à une commande.</summary>
    private static async Task<IResult> ByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListShipmentsByOrderQuery(orderId), ct)).Match(s => Results.Ok(s));

    /// <summary>Détail d'une expédition.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetShipmentQuery(id), ct)).Match(s => Results.Ok(s));

    /// <summary>Marque « en préparation ».</summary>
    private static async Task<IResult> PrepareAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkShipmentPreparingCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Marque « expédiée » (transporteur + numéro de suivi).</summary>
    private static async Task<IResult> ShipAsync(Guid id, ShipRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkShipmentShippedCommand(id, request.Carrier, request.TrackingNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Marque « livrée » (déclenche la levée d'escrow + reversement en aval).</summary>
    private static async Task<IResult> DeliverAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkShipmentDeliveredCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Annule l'expédition.</summary>
    private static async Task<IResult> CancelAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelShipmentCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record ShipRequest(string Carrier, string TrackingNumber);
}
