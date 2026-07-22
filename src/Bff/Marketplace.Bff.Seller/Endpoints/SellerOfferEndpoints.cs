using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Commands.ApplyOfferDiscount;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;
using Marketplace.Modules.Offers.Application.Offers.Commands.RemoveOfferDiscount;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;
using Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;
using Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;
using Marketplace.Modules.Offers.Application.Offers.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — offres. Liste scopée à la boutique du jeton ; chaque mutation par
/// id vérifie d'abord que l'offre appartient au vendeur (anti-IDOR).
/// </summary>
public static class SellerOfferEndpoints
{
    public static IEndpointRouteBuilder MapSellerOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var offers = app.MapSellerGroup("/seller/offers", "Vendeur · Offres");

        offers.MapGet("/", ListMineAsync);
        offers.MapGet("/{id:guid}", GetAsync);
        offers.MapPost("/", CreateAsync);
        offers.MapPatch("/{id:guid}/price", ChangePriceAsync);
        offers.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        offers.MapPatch("/{id:guid}/handling-time", SetHandlingTimeAsync);
        // Remise (promo vendeur) : appliquer / retirer.
        offers.MapPost("/{id:guid}/discount", ApplyDiscountAsync);
        offers.MapDelete("/{id:guid}/discount", RemoveDiscountAsync);

        return app;
    }

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListOffersBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            return result.Match(o => Results.Ok(o));
        }

        return Results.Ok(await EnrichAsync(result.Value, catalog, ct));
    }

    /// <summary>Détail d'une offre du vendeur (scopé : 404 si elle ne lui appartient pas).</summary>
    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        if (!await SellerBff.OwnsOfferAsync(sender, seller.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("offer");
        }

        var result = await sender.Send(new GetOfferQuery(id), ct);
        if (result.IsFailure)
        {
            return result.Match(o => Results.Ok(o));
        }

        var enriched = await EnrichAsync(new[] { result.Value }, catalog, ct);
        return Results.Ok(enriched[0]);
    }

    /// <summary>
    /// Attache à chaque offre le NOM de son produit.
    ///
    /// <c>OfferSummary</c> ne porte qu'un <c>ProductId</c> — un GUID. Les fronts
    /// affichaient donc un générique « Produit » sur TOUTES les offres, ce qui rend
    /// l'écran inutilisable : le vendeur ne peut pas savoir quel prix il modifie.
    /// Le module Offers ne connaît pas le catalogue, et c'est très bien ainsi ;
    /// recoller les deux vues est précisément le travail du BFF — c'est déjà ce qu'il
    /// fait pour les commandes et les expéditions.
    ///
    /// Résolution DÉDUPLIQUÉE par produit : un vendeur a souvent plusieurs offres sur
    /// le même article (états, entrepôts), et il serait absurde de relire dix fois la
    /// même fiche.
    /// </summary>
    private static async Task<List<SellerOfferView>> EnrichAsync(
        IReadOnlyCollection<OfferSummary> offers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var productId in offers.Select(o => o.ProductId).Distinct())
        {
            var product = await catalog.GetProductAsync(productId, ct);
            if (product is not null)
            {
                names[productId] = product.Name;
            }
        }

        return offers.Select(o => new SellerOfferView(
            o.Id, o.ProductId,
            // Produit supprimé mais offre encore là : on le DIT, plutôt que d'afficher
            // un nom rassurant qui masquerait une incohérence de catalogue.
            names.TryGetValue(o.ProductId, out var name) ? name : "Produit introuvable",
            o.SellerId, o.Sku, o.BasePriceAmount, o.Currency, o.Condition, o.FulfillmentType,
            o.ShipFromLocationId, o.HandlingTime, o.Status,
            o.SellerPrice, o.CommissionAmount, o.ProviderFeeAmount, o.ProductPrice,
            o.CompareAtAmount, o.DiscountEndsOnUtc)).ToList();
    }

    private static async Task<IResult> CreateAsync(ClaimsPrincipal user, CreateOfferRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new CreateOfferCommand(
            request.ProductId, seller.Id, request.Sku, request.SellerPrice, request.Currency,
            request.Condition, request.FulfillmentType, request.ShipFromLocationId, request.HandlingTime), ct);

        return result.Match(id => Results.Ok(new { offerId = id }));
    }

    private static async Task<IResult> ChangePriceAsync(ClaimsPrincipal user, Guid id, PriceRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferPriceCommand(id, request.SellerPrice), ct));

    private static async Task<IResult> ChangeStatusAsync(ClaimsPrincipal user, Guid id, StatusRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferStatusCommand(id, request.Status), ct));

    private static async Task<IResult> SetHandlingTimeAsync(ClaimsPrincipal user, Guid id, HandlingTimeRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new SetHandlingTimeCommand(id, request.HandlingTime), ct));

    private static async Task<IResult> ApplyDiscountAsync(ClaimsPrincipal user, Guid id, DiscountRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ApplyOfferDiscountCommand(id, request.Type, request.Value, request.EndsOnUtc), ct));

    private static async Task<IResult> RemoveDiscountAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new RemoveOfferDiscountCommand(id), ct));

    /// <summary>Résout le vendeur, vérifie la propriété de l'offre, puis exécute la commande.</summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid offerId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        if (!await SellerBff.OwnsOfferAsync(sender, seller.Id, offerId, ct))
        {
            return SellerBff.NotFoundOrForbidden("offer");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Offre vue par le vendeur : l'<c>OfferSummary</c> du module, plus le nom du
    /// produit. Mêmes champs, même ordre — l'ajout est purement additif, les clients
    /// déjà déployés continuent de désérialiser sans broncher.
    /// </summary>
    public sealed record SellerOfferView(
        Guid Id,
        Guid ProductId,
        string ProductName,
        Guid SellerId,
        string Sku,
        decimal BasePriceAmount,
        string Currency,
        string Condition,
        string FulfillmentType,
        Guid ShipFromLocationId,
        int HandlingTime,
        string Status,
        decimal SellerPrice,
        decimal CommissionAmount,
        decimal ProviderFeeAmount,
        decimal ProductPrice,
        decimal? CompareAtAmount,
        DateTime? DiscountEndsOnUtc);

    public sealed record CreateOfferRequest(
        Guid ProductId, string Sku, decimal SellerPrice, string Currency, string Condition,
        string FulfillmentType, Guid ShipFromLocationId, int HandlingTime);

    public sealed record PriceRequest(decimal SellerPrice);
    public sealed record StatusRequest(string Status);
    public sealed record HandlingTimeRequest(int HandlingTime);
    public sealed record DiscountRequest(string Type, decimal Value, DateTime? EndsOnUtc);
}
