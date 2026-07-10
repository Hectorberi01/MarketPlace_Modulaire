using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Notifications.Application.Devices;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>Corps d'enregistrement d'un appareil pour le push (FCM).</summary>
public sealed record DeviceRegisterRequest(string Token, string? Platform);

/// <summary>Corps de désenregistrement d'un appareil.</summary>
public sealed record DeviceUnregisterRequest(string Token);

/// <summary>
/// BFF Vendeur — notifications in-app. Scopées à l'utilisateur du jeton
/// (le compte propriétaire de la boutique). On réutilise à l'identique les
/// queries/commandes du module Notifications employées par le BFF Mobile ;
/// aucun code métier propre au vendeur n'est introduit ici.
/// </summary>
public static class SellerNotificationEndpoints
{
    public static IEndpointRouteBuilder MapSellerNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/seller/notifications")
            .WithTags("Vendeur · Notifications")
            .RequireAuthorization();

        notifications.MapGet("/", ListAsync);
        notifications.MapGet("/unread-count", UnreadCountAsync);
        notifications.MapPost("/{id:guid}/read", MarkReadAsync);
        notifications.MapPost("/read-all", MarkAllReadAsync);
        // Push : enregistrement/retrait du jeton d'appareil (FCM).
        notifications.MapPost("/devices", RegisterDeviceAsync);
        notifications.MapPost("/devices/unregister", UnregisterDeviceAsync);

        return app;
    }

    /// <summary>Enregistre le jeton d'appareil du vendeur pour recevoir les push.</summary>
    private static async Task<IResult> RegisterDeviceAsync(ClaimsPrincipal user, DeviceRegisterRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterDeviceTokenCommand(userId, request.Token, request.Platform ?? "unknown"), ct)).Match(() => Results.NoContent());

    /// <summary>Retire un jeton d'appareil (déconnexion / désabonnement push).</summary>
    private static async Task<IResult> UnregisterDeviceAsync(ClaimsPrincipal user, DeviceUnregisterRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is null
            ? Results.Unauthorized()
            : (await sender.Send(new UnregisterDeviceTokenCommand(request.Token), ct)).Match(() => Results.NoContent());

    /// <summary>Notifications de l'utilisateur du vendeur (les plus récentes d'abord).</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId), ct)).Match(n => Results.Ok(n));

    /// <summary>Nombre de notifications non lues.</summary>
    private static async Task<IResult> UnreadCountAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUnreadCountQuery(userId), ct)).Match(c => Results.Ok(new { unread = c }));

    /// <summary>Marque une notification comme lue (le module vérifie qu'elle appartient à l'utilisateur).</summary>
    private static async Task<IResult> MarkReadAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkNotificationReadCommand(id, userId), ct)).Match(() => Results.NoContent());

    /// <summary>Marque toutes les notifications de l'utilisateur comme lues.</summary>
    private static async Task<IResult> MarkAllReadAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkAllNotificationsReadCommand(userId), ct)).Match(() => Results.NoContent());
}
