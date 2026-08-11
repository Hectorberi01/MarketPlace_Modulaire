using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Inventory.Application.Locations.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Inventory : stock par SKU/localisation, réservations
/// (cycle de la commande), lieux d'expédition, disponibilité.
/// </summary>
public static class InventoryEndpoints
{
    /// <summary>Enregistre les routes du stock, des réservations et des lieux.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        MapItems(app);
        MapReservations(app);
        MapLocations(app);
        return app;
    }

    // ------------------------------------------------------------------ Items

    private static void MapItems(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/inventory/items").WithTags("Inventory · Stock").RequireAuthorization();

        group.MapPost("/", CreateItemAsync).WithName("CreateInventoryItem");
        group.MapGet("/{id:guid}", GetItemAsync).WithName("GetInventoryItem");
        group.MapPost("/{id:guid}/receive", ReceiveAsync).WithName("ReceiveStock");
        group.MapPost("/{id:guid}/adjust", AdjustAsync).WithName("AdjustStock");
        group.MapPut("/{id:guid}/reorder-threshold", SetThresholdAsync).WithName("SetReorderThreshold");

        app.MapGet("/api/inventory/availability/{sku}", GetAvailabilityAsync)
            .WithTags("Inventory · Stock").WithName("GetAvailability").AllowAnonymous();

        app.MapGet("/api/inventory/low-stock", ListLowStockAsync)
            .WithTags("Inventory · Stock").WithName("ListLowStock")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));
    }

    /// <summary>Crée un article de stock pour un SKU sur une localisation.</summary>
    private static async Task<IResult> CreateItemAsync(CreateItemRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateInventoryItemCommand(request.Sku, request.LocationId, request.OnHand, request.ReorderThreshold), ct);
        return result.Match(id => Results.Created($"/api/inventory/items/{id}", new { id }));
    }

    /// <summary>Récupère un article de stock (avec disponible calculé).</summary>
    private static async Task<IResult> GetItemAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetInventoryItemQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Ajoute du stock physique (réception).</summary>
    private static async Task<IResult> ReceiveAsync(Guid id, QuantityRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ReceiveStockCommand(id, request.Quantity), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Ajuste le stock physique (inventaire, casse, retour).</summary>
    private static async Task<IResult> AdjustAsync(Guid id, DeltaRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new AdjustStockCommand(id, request.Delta), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Modifie le seuil d'alerte de réapprovisionnement.</summary>
    private static async Task<IResult> SetThresholdAsync(Guid id, ThresholdRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Disponibilité agrégée d'un SKU (toutes localisations).</summary>
    private static async Task<IResult> GetAvailabilityAsync(string sku, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAvailabilityQuery(sku), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Liste les articles sous le seuil de réapprovisionnement (Admin).</summary>
    private static async Task<IResult> ListLowStockAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListLowStockQuery(), ct);
        return result.Match(items => Results.Ok(items));
    }

    // ----------------------------------------------------------- Reservations

    private static void MapReservations(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/inventory/reservations").WithTags("Inventory · Réservations").RequireAuthorization();

        group.MapPost("/", ReserveAsync).WithName("ReserveStock");
        group.MapPost("/release", ReleaseAsync).WithName("ReleaseReservation");
        group.MapPost("/confirm", ConfirmAsync).WithName("ConfirmReservation");
    }

    /// <summary>Réserve du stock pour une commande (étape Saga d'Ordering).</summary>
    private static async Task<IResult> ReserveAsync(ReserveRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ReserveStockCommand(request.Sku, request.LocationId, request.OrderId, request.Quantity), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Libère la réservation d'une commande (paiement échoué / annulation).</summary>
    private static async Task<IResult> ReleaseAsync(OrderRefRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ReleaseReservationCommand(request.Sku, request.LocationId, request.OrderId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Confirme la vente : décrémente le stock et solde la réservation.</summary>
    private static async Task<IResult> ConfirmAsync(OrderRefRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmReservationCommand(request.Sku, request.LocationId, request.OrderId), ct);
        return result.Match(() => Results.NoContent());
    }

    // --------------------------------------------------------------- Locations

    private static void MapLocations(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/inventory/locations").WithTags("Inventory · Lieux").RequireAuthorization();

        group.MapPost("/", CreateLocationAsync).WithName("CreateFulfillmentLocation");
        group.MapPut("/{id:guid}/address", UpdateLocationAddressAsync).WithName("UpdateLocationAddress");
    }

    /// <summary>Crée un lieu d'expédition (adresse vendeur FBS ou entrepôt plateforme FBP).</summary>
    private static async Task<IResult> CreateLocationAsync(CreateLocationRequest request, ISender sender, CancellationToken ct)
    {
        var command = new CreateFulfillmentLocationCommand(
            request.Type, request.OwnerId, request.Commune, request.Quartier, request.Landmark,
            request.Line, request.Latitude, request.Longitude, request.ContactPhone);
        var result = await sender.Send(command, ct);
        return result.Match(id => Results.Created($"/api/inventory/locations/{id}", new { id }));
    }

    /// <summary>Met à jour l'adresse d'un lieu d'expédition.</summary>
    private static async Task<IResult> UpdateLocationAddressAsync(Guid id, AddressRequest request, ISender sender, CancellationToken ct)
    {
        var command = new UpdateLocationAddressCommand(
            id, request.Commune, request.Quartier, request.Landmark, request.Line,
            request.Latitude, request.Longitude, request.ContactPhone);
        var result = await sender.Send(command, ct);
        return result.Match(() => Results.NoContent());
    }

    // ------------------------------------------------------- Contrats d'entrée

    public sealed record CreateItemRequest(string Sku, Guid LocationId, int OnHand, int ReorderThreshold);
    public sealed record QuantityRequest(int Quantity);
    public sealed record DeltaRequest(int Delta);
    public sealed record ThresholdRequest(int Threshold);
    public sealed record ReserveRequest(string Sku, Guid LocationId, Guid OrderId, int Quantity);
    public sealed record OrderRefRequest(string Sku, Guid LocationId, Guid OrderId);
    // « Commune » accepte le code (« abomey-calavi ») comme le libellé (« Abomey-Calavi »).
    public sealed record CreateLocationRequest(
        string Type, Guid? OwnerId, string? Commune, string? Quartier, string? Landmark,
        string? Line, double? Latitude, double? Longitude, string? ContactPhone = null);

    public sealed record AddressRequest(
        string? Commune, string? Quartier, string? Landmark, string? Line,
        double? Latitude, double? Longitude, string? ContactPhone = null);
}
