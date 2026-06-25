using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — panier (/mobile/cart/*). Scopé à l'acheteur du jeton.</summary>
public static class MobileCartEndpoints
{
    public static IEndpointRouteBuilder MapMobileCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/mobile/cart").WithTags("Mobile · Panier").RequireAuthorization();

        cart.MapGet("/", GetAsync);
        cart.MapPost("/items", AddItemAsync);
        cart.MapPatch("/items/{offerId:guid}", UpdateItemAsync);
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync);

        // Code promo au niveau panier : nécessite une commande Cart dédiée (le cumul
        // de promos vit dans Pricing ; l'application au panier reste à exposer).
        cart.MapPost("/coupon", () => MobileBff.NotImplemented("cart.apply_coupon"));
        cart.MapDelete("/coupon", () => MobileBff.NotImplemented("cart.apply_coupon"));

        return app;
    }

    /// <summary>Panier courant valorisé (multi-vendeur, sous-total, promos appliquées).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveCartQuery(buyerId), ct)).Match(c => Results.Ok(c));

    /// <summary>Ajoute une offre au panier.</summary>
    private static async Task<IResult> AddItemAsync(ClaimsPrincipal user, AddItemRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct)).Match(id => Results.Ok(new { cartId = id }));

    /// <summary>Modifie la quantité d'une ligne.</summary>
    private static async Task<IResult> UpdateItemAsync(ClaimsPrincipal user, Guid offerId, QuantityRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct)).Match(() => Results.NoContent());

    /// <summary>Retire une ligne du panier.</summary>
    private static async Task<IResult> RemoveItemAsync(ClaimsPrincipal user, Guid offerId, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct)).Match(() => Results.NoContent());

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record QuantityRequest(int Quantity);
}
