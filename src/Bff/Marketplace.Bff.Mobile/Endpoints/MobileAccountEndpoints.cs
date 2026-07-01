using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.PaymentMethods;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Wishlist.Application.Wishlists;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — compte, favoris et notifications (scopés au jeton).</summary>
public static class MobileAccountEndpoints
{
    public static IEndpointRouteBuilder MapMobileAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/mobile").WithTags("Mobile · Compte").RequireAuthorization();

        mobile.MapGet("/account/profile", ProfileAsync);
        mobile.MapPut("/account/profile", UpdateProfileAsync);

        mobile.MapGet("/wishlist", WishlistAsync);
        mobile.MapPost("/wishlist/items", AddWishlistAsync);
        mobile.MapDelete("/wishlist/items/{productId:guid}", RemoveWishlistAsync);

        mobile.MapGet("/notifications", NotificationsAsync);
        mobile.MapPatch("/notifications/{id:guid}/read", MarkReadAsync);

        // Moyens de paiement enregistrés (Mobile Money + cartes masquées).
        mobile.MapGet("/account/payment-methods", ListPaymentMethodsAsync);
        mobile.MapPost("/account/payment-methods", AddPaymentMethodAsync);
        mobile.MapDelete("/account/payment-methods/{id:guid}", DeletePaymentMethodAsync);
        mobile.MapPut("/account/payment-methods/{id:guid}/default", SetDefaultPaymentMethodAsync);

        // Préférences de notification et enregistrement de token push (FCM/APNs) :
        // capacités Notifications à exposer (NotificationPreference + device tokens).
        mobile.MapPut("/notifications/preferences", () => MobileBff.NotImplemented("notifications.preferences"));
        mobile.MapPost("/devices", () => MobileBff.NotImplemented("notifications.push_devices"));

        return app;
    }

    /// <summary>Profil utilisateur.</summary>
    private static async Task<IResult> ProfileAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserQuery(userId), ct)).Match(u => Results.Ok(u));

    /// <summary>Met à jour le profil.</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Liste d'envies.</summary>
    private static async Task<IResult> WishlistAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyWishlistQuery(userId), ct)).Match(w => Results.Ok(w));

    /// <summary>Ajoute un produit aux favoris.</summary>
    private static async Task<IResult> AddWishlistAsync(ClaimsPrincipal user, WishlistRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AddToWishlistCommand(userId, request.ProductId, request.OfferId, request.PriceAlert, request.StockAlert), ct)).Match(() => Results.NoContent());

    /// <summary>Retire des favoris.</summary>
    private static async Task<IResult> RemoveWishlistAsync(ClaimsPrincipal user, Guid productId, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFromWishlistCommand(userId, productId), ct)).Match(() => Results.NoContent());

    /// <summary>Notifications in-app.</summary>
    private static async Task<IResult> NotificationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId), ct)).Match(n => Results.Ok(n));

    /// <summary>Marque une notification comme lue.</summary>
    private static async Task<IResult> MarkReadAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkNotificationReadCommand(id, userId), ct)).Match(() => Results.NoContent());

    /// <summary>Liste les moyens de paiement de l'utilisateur (défaut en tête).</summary>
    private static async Task<IResult> ListPaymentMethodsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListPaymentMethodsQuery(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Enregistre un moyen de paiement (Mobile Money ou carte).</summary>
    private static async Task<IResult> AddPaymentMethodAsync(ClaimsPrincipal user, PaymentMethodRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddPaymentMethodCommand(
            userId, request.Type, request.Label, request.Provider, request.Msisdn,
            request.CardNumber, request.ExpiryMonth, request.ExpiryYear, request.HolderName,
            request.MakeDefault), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Supprime un moyen de paiement.</summary>
    private static async Task<IResult> DeletePaymentMethodAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeletePaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    /// <summary>Définit le moyen de paiement par défaut.</summary>
    private static async Task<IResult> SetDefaultPaymentMethodAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetDefaultPaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record WishlistRequest(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert);

    public sealed record PaymentMethodRequest(
        string Type,
        string? Label,
        string Provider,
        string? Msisdn,
        string? CardNumber,
        int? ExpiryMonth,
        int? ExpiryYear,
        string? HolderName,
        bool MakeDefault);
}
