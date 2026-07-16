using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Inventory.Application.Locations.Commands;
using Marketplace.Modules.Inventory.Application.Locations.Queries;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — lieux d'expédition &amp; stock. Le lieu est créé au nom de la
/// boutique du jeton (OwnerId = SellerId) ; son id sert ensuite de
/// « ShipFromLocationId » à la création d'offre.
///
/// Limite connue : le module Inventory ne lie pas encore les articles de stock à
/// un vendeur, donc les opérations sur un article par id ne sont pas scopées
/// vendeur (le vendeur agit sur les ids retournés à la création). Les requêtes
/// globales (low-stock, get par id) ne sont volontairement PAS exposées ici pour
/// ne pas divulguer le stock d'autres vendeurs.
/// </summary>
public static class SellerInventoryEndpoints
{
    public static IEndpointRouteBuilder MapSellerInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var locations = app.MapSellerGroup("/seller/locations", "Vendeur · Lieux");
        locations.MapGet("/", ListLocationsAsync);
        locations.MapPost("/", CreateLocationAsync);
        locations.MapDelete("/{id:guid}", DeleteLocationAsync);

        var stock = app.MapSellerGroup("/seller/inventory", "Vendeur · Stock");
        stock.MapPost("/items", CreateItemAsync);
        stock.MapPost("/items/{id:guid}/receive", ReceiveAsync);
        stock.MapPost("/items/{id:guid}/adjust", AdjustAsync);
        stock.MapPut("/items/{id:guid}/reorder-threshold", SetThresholdAsync);
        stock.MapGet("/availability/{sku}", AvailabilityAsync);
        stock.MapGet("/by-sku/{sku}", ListBySkuAsync);

        return app;
    }

    /// <summary>Liste les articles de stock d'un SKU (toutes localisations).</summary>
    private static async Task<IResult> ListBySkuAsync(string sku, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListInventoryBySkuQuery(sku), ct)).Match(items => Results.Ok(items));

    /// <summary>Liste les lieux d'expédition de la boutique du jeton (OwnerId = SellerId).</summary>
    private static async Task<IResult> ListLocationsAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListFulfillmentLocationsQuery(seller.Id), ct)).Match(l => Results.Ok(l));
    }

    /// <summary>Crée un lieu d'expédition rattaché à la boutique du jeton (OwnerId = SellerId).</summary>
    private static async Task<IResult> CreateLocationAsync(ClaimsPrincipal user, LocationRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new CreateFulfillmentLocationCommand(
            "SellerAddress", seller.Id, request.Line, request.City, request.Country, request.Latitude, request.Longitude), ct);

        return result.Match(id => Results.Ok(new { locationId = id }));
    }

    /// <summary>
    /// Supprime un lieu d'expédition de la boutique du jeton. La suppression passe
    /// l'OwnerId (= SellerId) à la commande : le module ne supprime que si le lieu
    /// appartient bien à cette boutique (anti-IDOR ; 404 normalisé sinon).
    /// </summary>
    private static async Task<IResult> DeleteLocationAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new DeleteFulfillmentLocationCommand(id, seller.Id), ct);
        return result.Match(() => Results.NoContent());
    }

    private static async Task<IResult> CreateItemAsync(ClaimsPrincipal user, CreateItemRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new CreateInventoryItemCommand(request.Sku, request.LocationId, request.OnHand, request.ReorderThreshold), ct);
        return result.Match(id => Results.Ok(new { inventoryItemId = id }));
    }

    private static async Task<IResult> ReceiveAsync(ClaimsPrincipal user, Guid id, QuantityRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardSellerAsync(user, sellers, ct, () => sender.Send(new ReceiveStockCommand(id, request.Quantity), ct));

    private static async Task<IResult> AdjustAsync(ClaimsPrincipal user, Guid id, DeltaRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardSellerAsync(user, sellers, ct, () => sender.Send(new AdjustStockCommand(id, request.Delta), ct));

    private static async Task<IResult> SetThresholdAsync(ClaimsPrincipal user, Guid id, ThresholdRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardSellerAsync(user, sellers, ct, () => sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct));

    /// <summary>Disponibilité agrégée d'un SKU (le vendeur interroge ses propres SKU).</summary>
    private static async Task<IResult> AvailabilityAsync(ClaimsPrincipal user, string sku, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new GetAvailabilityQuery(sku), ct)).Match(a => Results.Ok(a));
    }

    /// <summary>Exige une boutique active, puis exécute (l'ownership fin de l'article relève du module).</summary>
    private static async Task<IResult> GuardSellerAsync(
        ClaimsPrincipal user, ISellerModuleApi sellers, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null ? SellerBff.NotASeller() : (await action()).Match(() => Results.NoContent());
    }

    public sealed record LocationRequest(string Line, string City, string Country, double? Latitude, double? Longitude);
    public sealed record CreateItemRequest(string Sku, Guid LocationId, int OnHand, int ReorderThreshold);
    public sealed record QuantityRequest(int Quantity);
    public sealed record DeltaRequest(int Delta);
    public sealed record ThresholdRequest(int Threshold);
}
