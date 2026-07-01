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
/// BFF Vendeur — offres. Liste scopée à la boutique du jeton ; chaque mutation par
/// id vérifie d'abord que l'offre appartient au vendeur (anti-IDOR).
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

    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListOffersBySellerQuery(seller.Id), ct)).Match(o => Results.Ok(o));
    }

    /// <summary>Détail d'une offre du vendeur (scopé : 404 si elle ne lui appartient pas).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return await SellerBff.OwnsOfferAsync(sender, seller.Id, id, ct)
            ? (await sender.Send(new GetOfferQuery(id), ct)).Match(o => Results.Ok(o))
            : SellerBff.NotFoundOrForbidden("offer");
    }

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

    private static async Task<IResult> ChangePriceAsync(ClaimsPrincipal user, Guid id, PriceRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferPriceCommand(id, request.Amount, request.Currency), ct));

    private static async Task<IResult> ChangeStatusAsync(ClaimsPrincipal user, Guid id, StatusRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferStatusCommand(id, request.Status), ct));

    private static async Task<IResult> SetHandlingTimeAsync(ClaimsPrincipal user, Guid id, HandlingTimeRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new SetHandlingTimeCommand(id, request.HandlingTime), ct));

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

    public sealed record CreateOfferRequest(
        Guid ProductId, string Sku, decimal BasePriceAmount, string Currency, string Condition,
        string FulfillmentType, Guid ShipFromLocationId, int HandlingTime);

    public sealed record PriceRequest(decimal Amount, string Currency);
    public sealed record StatusRequest(string Status);
    public sealed record HandlingTimeRequest(int HandlingTime);
}
