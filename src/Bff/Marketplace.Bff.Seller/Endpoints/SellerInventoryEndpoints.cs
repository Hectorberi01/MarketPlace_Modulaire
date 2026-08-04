using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Inventory.Contracts;
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
/// ─────────────────────────────────────────────────────────────────────────────────
/// LA PROPRIÉTÉ D'UN ARTICLE DE STOCK SE DÉDUIT DE SA LOCALISATION.
///
/// Le module Inventory ne rattache pas les articles à un vendeur : il ne connaît que
/// des localisations, qui portent un `OwnerId`. Ce fichier affirmait donc que
/// l'ownership « relève du module » — or le module ne le vérifiait nulle part, et le
/// garde local se contentait de constater que l'appelant possédait *une* boutique.
///
/// Résultat : `POST /seller/inventory/items/{id}/adjust {delta:-99999}` mettait en
/// rupture le catalogue de n'importe quel concurrent. Et il n'y avait même pas à
/// deviner les identifiants — `GET /by-sku/{sku}` les livrait, sans authentification,
/// à partir d'un SKU public exposé par le BFF Mobile.
///
/// Le chaînage article → localisation → `OwnerId` reconstitue la propriété sans
/// toucher au module. C'est un aller-retour de plus par écriture ; à ce prix, chaque
/// mutation par id est enfin scopée.
///
/// La vraie correction est structurelle : porter le `SellerId` sur l'article lui-même.
/// Elle demande une migration et la reprise des données, donc elle attendra — mais
/// elle rendrait ces gardes inutiles plutôt que nécessaires.
/// ─────────────────────────────────────────────────────────────────────────────────
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
        stock.MapGet("/", ListMineAsync);
        stock.MapPost("/items", CreateItemAsync);
        stock.MapPost("/items/{id:guid}/receive", ReceiveAsync);
        stock.MapPost("/items/{id:guid}/adjust", AdjustAsync);
        stock.MapPut("/items/{id:guid}/reorder-threshold", SetThresholdAsync);
        stock.MapGet("/availability/{sku}", AvailabilityAsync);
        stock.MapGet("/by-sku/{sku}", ListBySkuAsync);

        return app;
    }

    /// <summary>
    /// TOUT le stock de la boutique du jeton, toutes localisations confondues.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Cette route manquait, et son absence était structurante : on ne pouvait
    /// consulter du stock qu'en partant d'un SKU précis (`/by-sku/{sku}`). Un écran
    /// « Stock » devait donc appeler cette route une fois par référence du catalogue —
    /// autant de requêtes que de SKU, à chaque affichage — pour reconstituer une liste
    /// que la base sait produire d'un coup.
    ///
    /// Le filtrage par localisation se fait EN BASE, via `ListInventoryByLocationsQuery`.
    /// Il repose sur le même chaînage que les gardes de ce fichier : article →
    /// localisation → `OwnerId`. Aucun article d'un concurrent ne peut donc entrer dans
    /// la réponse, pour la même raison qu'aucun ne peut être modifié.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        // ⚠️ On interroge le module DIRECTEMENT, sans passer par `SellerLocationIdsAsync`.
        //
        // Ce helper renvoie un ensemble VIDE quand la requête échoue. C'est le bon
        // comportement sur les chemins d'ÉCRITURE, où il fait échouer fermé : aucune
        // localisation connue, donc aucune mutation autorisée.
        //
        // En LECTURE, il produirait l'inverse : une panne du service des lieux
        // deviendrait un 200 avec une liste vide, que l'écran affiche « aucune référence
        // suivie ». Un vendeur conclurait que son stock a disparu. On distingue donc
        // explicitement l'échec de l'absence.
        var locations = await sender.Send(new ListFulfillmentLocationsQuery(seller.Id), ct);
        if (locations.IsFailure)
        {
            return locations.Match(l => Results.Ok(l));
        }

        var mine = locations.Value.Select(l => l.Id).ToList();
        if (mine.Count == 0)
        {
            return Results.Ok(Array.Empty<InventoryItemSummary>());
        }

        return (await sender.Send(new ListInventoryByLocationsQuery(mine), ct)).Match(i => Results.Ok(i));
    }

    /// <summary>
    /// Articles de stock d'un SKU, RESTREINTS AUX LOCALISATIONS DU VENDEUR.
    ///
    /// Cette route n'avait ni `ClaimsPrincipal` ni résolution de vendeur : elle rendait
    /// les identifiants, localisations et quantités de tous les vendeurs pour n'importe
    /// quel SKU — et les SKU sont publics côté acheteur.
    /// </summary>
    private static async Task<IResult> ListBySkuAsync(
        ClaimsPrincipal user, string sku, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListInventoryBySkuQuery(sku), ct);
        if (result.IsFailure)
        {
            return result.Match(items => Results.Ok(items));
        }

        var mine = await SellerLocationIdsAsync(sender, seller.Id, ct);
        return Results.Ok(result.Value.Where(i => mine.Contains(i.LocationId)).ToList());
    }

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
            "SellerAddress", seller.Id, request.Commune, request.Quartier, request.Landmark,
            request.Line, request.Latitude, request.Longitude), ct);

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

        // La localisation d'accueil doit appartenir au vendeur : sans ce contrôle, on
        // rattachait du stock à l'entrepôt d'un concurrent, faussant ses alertes de
        // réapprovisionnement.
        var mine = await SellerLocationIdsAsync(sender, seller.Id, ct);
        if (!mine.Contains(request.LocationId))
        {
            return SellerBff.NotFoundOrForbidden("location");
        }

        var result = await sender.Send(new CreateInventoryItemCommand(request.Sku, request.LocationId, request.OnHand, request.ReorderThreshold), ct);
        return result.Match(id => Results.Ok(new { inventoryItemId = id }));
    }

    private static async Task<IResult> ReceiveAsync(ClaimsPrincipal user, Guid id, QuantityRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new ReceiveStockCommand(id, request.Quantity), ct));

    private static async Task<IResult> AdjustAsync(ClaimsPrincipal user, Guid id, DeltaRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new AdjustStockCommand(id, request.Delta), ct));

    private static async Task<IResult> SetThresholdAsync(ClaimsPrincipal user, Guid id, ThresholdRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct));

    /// <summary>
    /// Disponibilité agrégée d'un SKU — uniquement pour un SKU que le vendeur propose.
    ///
    /// La requête sous-jacente est globale. Sans ce filtre, un vendeur surveillait en
    /// continu le stock disponible de ses concurrents : une donnée concurrentielle
    /// directe, obtenue à partir d'un SKU public.
    /// </summary>
    private static async Task<IResult> AvailabilityAsync(
        ClaimsPrincipal user, string sku, ISender sender, ISellerModuleApi sellers,
        IOfferModuleApi offers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var sellsThisSku = (await offers.GetOffersBySkuAsync(sku, ct)).Any(o => o.SellerId == seller.Id);
        if (!sellsThisSku)
        {
            return SellerBff.NotFoundOrForbidden("sku");
        }

        return (await sender.Send(new GetAvailabilityQuery(sku), ct)).Match(a => Results.Ok(a));
    }

    /// <summary>
    /// Exige une boutique active ET que l'article de stock lui appartienne, puis exécute.
    ///
    /// Remplace l'ancien `GuardSellerAsync`, qui ne vérifiait que la première moitié.
    /// </summary>
    private static async Task<IResult> GuardItemAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid itemId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var item = await sender.Send(new GetInventoryItemQuery(itemId), ct);
        if (item.IsFailure)
        {
            return SellerBff.NotFoundOrForbidden("inventory_item");
        }

        var mine = await SellerLocationIdsAsync(sender, seller.Id, ct);
        if (!mine.Contains(item.Value.LocationId))
        {
            // Même réponse qu'un article inexistant : distinguer les deux cas
            // confirmerait au passage l'existence du stock d'un concurrent.
            return SellerBff.NotFoundOrForbidden("inventory_item");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <summary>Identifiants des localisations appartenant à cette boutique.</summary>
    private static async Task<HashSet<Guid>> SellerLocationIdsAsync(ISender sender, Guid sellerId, CancellationToken ct)
    {
        var locations = await sender.Send(new ListFulfillmentLocationsQuery(sellerId), ct);
        return locations.IsFailure
            ? new HashSet<Guid>()
            : locations.Value.Select(l => l.Id).ToHashSet();
    }

    // « Commune » accepte indifféremment le code (« abomey-calavi ») ou le libellé
    // (« Abomey-Calavi ») : les consoles envoient le code, les imports des libellés.
    public sealed record LocationRequest(
        string? Commune, string? Quartier, string? Landmark, string? Line,
        double? Latitude, double? Longitude);
    public sealed record CreateItemRequest(string Sku, Guid LocationId, int OnHand, int ReorderThreshold);
    public sealed record QuantityRequest(int Quantity);
    public sealed record DeltaRequest(int Delta);
    public sealed record ThresholdRequest(int Threshold);
}
