using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Wishlist.Application.Wishlists;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>BFF Web — compte, favoris et notifications (scopés au jeton).</summary>
public static class WebAccountEndpoints
{
    public static IEndpointRouteBuilder MapWebAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var web = app.MapGroup("/web").WithTags("Web · Compte").RequireAuthorization();

        web.MapGet("/account/profile", ProfileAsync);
        web.MapPut("/account/profile", UpdateProfileAsync);

        web.MapGet("/wishlist", WishlistAsync);
        web.MapPost("/wishlist/items", AddWishlistAsync);
        web.MapDelete("/wishlist/items/{productId:guid}", RemoveWishlistAsync);

        web.MapGet("/notifications", NotificationsAsync);
        web.MapPatch("/notifications/{id:guid}/read", MarkReadAsync);

        // Préférences de notification : capacité Notifications à exposer
        // (NotificationPreference). Pas de device push côté web (canal navigateur distinct).
        web.MapPut("/notifications/preferences", () => WebBff.NotImplemented("notifications.preferences"));

        return app;
    }

    /// <summary>Profil utilisateur.</summary>
    private static async Task<IResult> ProfileAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserQuery(userId), ct)).Match(u => Results.Ok(u));

    /// <summary>Met à jour le profil.</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Liste d'envies.</summary>
    private static async Task<IResult> WishlistAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyWishlistQuery(userId), ct)).Match(w => Results.Ok(w));

    /// <summary>Ajoute un produit aux favoris.</summary>
    private static async Task<IResult> AddWishlistAsync(ClaimsPrincipal user, WishlistRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AddToWishlistCommand(userId, request.ProductId, request.OfferId, request.PriceAlert, request.StockAlert), ct)).Match(() => Results.NoContent());

    /// <summary>Retire des favoris.</summary>
    private static async Task<IResult> RemoveWishlistAsync(ClaimsPrincipal user, Guid productId, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFromWishlistCommand(userId, productId), ct)).Match(() => Results.NoContent());

    /// <summary>Notifications in-app.</summary>
    private static async Task<IResult> NotificationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId), ct)).Match(n => Results.Ok(n));

    /// <summary>Marque une notification comme lue.</summary>
    private static async Task<IResult> MarkReadAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkNotificationReadCommand(id, userId), ct)).Match(() => Results.NoContent());

    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record WishlistRequest(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert);
}
