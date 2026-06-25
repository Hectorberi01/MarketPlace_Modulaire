using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Wishlist.Application.Wishlists;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Wishlist : liste d'envies de l'utilisateur connecté,
/// avec alertes de baisse de prix ou de retour en stock.
/// </summary>
public static class WishlistEndpoints
{
    /// <summary>Enregistre les routes de la liste d'envies.</summary>
    public static IEndpointRouteBuilder MapWishlistEndpoints(this IEndpointRouteBuilder app)
    {
        var wishlist = app.MapGroup("/api/wishlist").WithTags("Wishlist").RequireAuthorization();

        wishlist.MapGet("/", GetAsync).WithName("GetMyWishlist");
        wishlist.MapPost("/items", AddAsync).WithName("AddToWishlist");
        wishlist.MapDelete("/items/{productId:guid}", RemoveAsync).WithName("RemoveFromWishlist");
        wishlist.MapPut("/items/{productId:guid}/alerts", AlertsAsync).WithName("SetWishlistAlerts");

        return app;
    }

    /// <summary>Renvoie la liste d'envies de l'utilisateur connecté.</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetMyWishlistQuery(userId), ct);
        return result.Match(w => Results.Ok(w));
    }

    /// <summary>Ajoute (ou met à jour) un produit dans la liste d'envies.</summary>
    private static async Task<IResult> AddAsync(AddBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddToWishlistCommand(userId, body.ProductId, body.OfferId, body.PriceAlert, body.StockAlert), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire un produit de la liste d'envies.</summary>
    private static async Task<IResult> RemoveAsync(Guid productId, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RemoveFromWishlistCommand(userId, productId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Active/désactive les alertes prix/stock d'un produit suivi.</summary>
    private static async Task<IResult> AlertsAsync(Guid productId, AlertsBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SetWishlistAlertsCommand(userId, productId, body.PriceAlert, body.StockAlert), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record AddBody(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert);
    public sealed record AlertsBody(bool PriceAlert, bool StockAlert);
}
