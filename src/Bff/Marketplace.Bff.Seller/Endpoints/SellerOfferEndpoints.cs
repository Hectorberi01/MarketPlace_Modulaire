using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;
using Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;
using Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;
using Marketplace.Modules.Offers.Application.Offers.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — offres : les listings du vendeur (prix, statut, délai de
/// préparation) attachés à des produits existants. La liste est scopée à la
/// boutique du jeton.
/// </summary>
public static class SellerOfferEndpoints
{
    public static IEndpointRouteBuilder MapSellerOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var offers = app.MapGroup("/seller/offers").WithTags("Vendeur · Offres").RequireAuthorization();

        offers.MapGet("/", ListMineAsync);
        offers.MapGet("/{id:guid}", GetAsync);
        offers.MapPost("/", CreateAsync);
        offers.MapPatch("/{id:guid}/price", ChangePriceAsync);
        offers.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        offers.MapPatch("/{id:guid}/handling-time", SetHandlingTimeAsync);

        return app;
    }

    /// <summary>Toutes les offres de la boutique du jeton.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListOffersBySellerQuery(seller.Id), ct)).Match(o => Results.Ok(o));
    }

    /// <summary>Détail d'une offre.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetOfferQuery(id), ct)).Match(o => Results.Ok(o));

    /// <summary>Crée une offre (boutique du jeton × produit) sur une variante.</summary>
    private static async Task<IResult> CreateAsync(ClaimsPrincipal user, CreateOfferRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new CreateOfferCommand(
            request.ProductId, seller.Id, request.Sku, request.BasePriceAmount, request.Currency,
            request.Condition, request.FulfillmentType, request.ShipFromLocationId, request.HandlingTime), ct);

        return result.Match(id => Results.Ok(new { offerId = id }));
    }

    /// <summary>Change le prix de base d'une offre.</summary>
    private static async Task<IResult> ChangePriceAsync(Guid id, PriceRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeOfferPriceCommand(id, request.Amount, request.Currency), ct)).Match(() => Results.NoContent());

    /// <summary>Change le statut d'une offre (Active, Paused, OutOfStock).</summary>
    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeOfferStatusCommand(id, request.Status), ct)).Match(() => Results.NoContent());

    /// <summary>Modifie le délai de préparation (jours).</summary>
    private static async Task<IResult> SetHandlingTimeAsync(Guid id, HandlingTimeRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetHandlingTimeCommand(id, request.HandlingTime), ct)).Match(() => Results.NoContent());

    public sealed record CreateOfferRequest(
        Guid ProductId, string Sku, decimal BasePriceAmount, string Currency, string Condition,
        string FulfillmentType, Guid ShipFromLocationId, int HandlingTime);

    public sealed record PriceRequest(decimal Amount, string Currency);
    public sealed record StatusRequest(string Status);
    public sealed record HandlingTimeRequest(int HandlingTime);
}
