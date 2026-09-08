using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Cart : le panier de l'acheteur connecté. Les lignes
/// sont valorisées à la lecture via Pricing (prix de base + promotions).
/// </summary>
public static class CartEndpoints
{
    /// <summary>Enregistre les routes du panier (acheteur authentifié).</summary>
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapAuthenticatedGroup("/api/cart").WithTags("Cart");

        cart.MapGet("/", GetAsync).WithName("GetCart");
        cart.MapPost("/items", AddItemAsync).WithName("AddCartItem");
        cart.MapPut("/items/{offerId:guid}", UpdateItemAsync).WithName("UpdateCartItem");
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync).WithName("RemoveCartItem");
        cart.MapDelete("/", ClearAsync).WithName("ClearCart");
        cart.MapPost("/checkout", CheckoutAsync).WithName("CheckoutCart");

        return app;
    }

    /// <summary>Renvoie le panier actif valorisé de l'acheteur connecté.</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return result.Match(cart => Results.Ok(cart));
    }

    /// <summary>Ajoute une offre au panier (création implicite du panier si besoin).</summary>
    private static async Task<IResult> AddItemAsync(AddItemRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct);
        return result.Match(cartId => Results.Ok(new { cartId }));
    }

    /// <summary>Change la quantité d'une ligne (0 la retire).</summary>
    private static async Task<IResult> UpdateItemAsync(Guid offerId, UpdateItemRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire une ligne du panier.</summary>
    private static async Task<IResult> RemoveItemAsync(Guid offerId, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Vide le panier actif.</summary>
    private static async Task<IResult> ClearAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ClearCartCommand(buyerId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Valide le panier (checkout) et renvoie l'identifiant du panier figé.</summary>
    private static async Task<IResult> CheckoutAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new CheckoutCartCommand(buyerId), ct);
        return result.Match(cartId => Results.Ok(new { cartId }));
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record UpdateItemRequest(int Quantity);
}
