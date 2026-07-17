using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>BFF Web — panier (/web/cart/*). Scopé à l'acheteur du jeton.</summary>
public static class WebCartEndpoints
{
    public static IEndpointRouteBuilder MapWebCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/web/cart").WithTags("Web · Panier").RequireAuthorization();

        cart.MapGet("/", GetAsync);
        cart.MapPost("/items", AddItemAsync);
        cart.MapPatch("/items/{offerId:guid}", UpdateItemAsync);
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync);

        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);

        return app;
    }

    /// <summary>
    /// Attache un code promo au panier, puis renvoie le panier revalorisé.
    ///
    /// Le BFF Web renvoyait ici un 501 — et c'était, paradoxalement, plus honnête que le
    /// BFF Mobile, qui affichait une remise fictive que l'acheteur ne recevait jamais.
    /// Un 501 dit « pas encore » ; l'autre disait « c'est fait » alors que rien n'était fait.
    /// </summary>
    private static async Task<IResult> ApplyCouponAsync(
        ClaimsPrincipal user, CouponRequest request, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var applied = await sender.Send(new ApplyCouponCommand(buyerId, request.Code ?? string.Empty), ct);
        if (applied.IsFailure)
        {
            return applied.Match(() => Results.NoContent());
        }

        var cart = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return cart.Match(c => Results.Ok(c));
    }

    /// <summary>Retire le code promo du panier et renvoie le panier revalorisé.</summary>
    private static async Task<IResult> RemoveCouponAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var removed = await sender.Send(new RemoveCouponCommand(buyerId), ct);
        if (removed.IsFailure)
        {
            return removed.Match(() => Results.NoContent());
        }

        var cart = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return cart.Match(c => Results.Ok(c));
    }

    public sealed record CouponRequest(string Code);

    /// <summary>Panier courant valorisé (multi-vendeur, sous-total, promos appliquées).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveCartQuery(buyerId), ct)).Match(c => Results.Ok(c));

    /// <summary>Ajoute une offre au panier.</summary>
    private static async Task<IResult> AddItemAsync(ClaimsPrincipal user, AddItemRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct)).Match(id => Results.Ok(new { cartId = id }));

    /// <summary>Modifie la quantité d'une ligne.</summary>
    private static async Task<IResult> UpdateItemAsync(ClaimsPrincipal user, Guid offerId, QuantityRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct)).Match(() => Results.NoContent());

    /// <summary>Retire une ligne du panier.</summary>
    private static async Task<IResult> RemoveItemAsync(ClaimsPrincipal user, Guid offerId, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct)).Match(() => Results.NoContent());

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record QuantityRequest(int Quantity);
}
