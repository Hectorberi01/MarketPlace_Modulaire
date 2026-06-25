using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Notifications : boîte de réception de l'utilisateur
/// connecté, alimentée par les events des autres modules (commande, expédition).
/// </summary>
public static class NotificationsEndpoints
{
    /// <summary>Enregistre les routes des notifications (utilisateur authentifié).</summary>
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("/", ListAsync).WithName("ListMyNotifications");
        notifications.MapGet("/unread-count", UnreadCountAsync).WithName("GetUnreadNotificationCount");
        notifications.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        notifications.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");

        return app;
    }

    /// <summary>Liste les notifications de l'utilisateur connecté.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyNotificationsQuery(userId), ct);
        return result.Match(notifications => Results.Ok(notifications));
    }

    /// <summary>Renvoie le nombre de notifications non lues.</summary>
    private static async Task<IResult> UnreadCountAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetUnreadCountQuery(userId), ct);
        return result.Match(count => Results.Ok(new { unread = count }));
    }

    /// <summary>Marque une notification comme lue.</summary>
    private static async Task<IResult> MarkReadAsync(Guid id, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new MarkNotificationReadCommand(id, userId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque toutes les notifications comme lues.</summary>
    private static async Task<IResult> MarkAllReadAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new MarkAllNotificationsReadCommand(userId), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
