using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Notifications.Application.Devices;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Preferences;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>Corps d'enregistrement d'un appareil pour le push (FCM).</summary>
public sealed record DeviceRegisterRequest(string Token, string? Platform);

/// <summary>Corps de désenregistrement d'un appareil.</summary>
public sealed record DeviceUnregisterRequest(string Token);

/// <summary>Corps de mise à jour des préférences : catégories dont le push est coupé.</summary>
public sealed record NotificationPreferencesRequest(List<string>? MutedCategories);

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
        var notifications = app.MapSellerGroup("/seller/notifications", "Vendeur · Notifications");

        notifications.MapGet("/", ListAsync);
        notifications.MapGet("/unread-count", UnreadCountAsync);
        notifications.MapPost("/{id:guid}/read", MarkReadAsync);
        notifications.MapPost("/read-all", MarkAllReadAsync);
        // Push : enregistrement/retrait du jeton d'appareil (FCM).
        notifications.MapPost("/devices", RegisterDeviceAsync);
        notifications.MapPost("/devices/unregister", UnregisterDeviceAsync);
        // Préférences : quelles catégories de push le vendeur souhaite recevoir.
        notifications.MapGet("/preferences", GetPreferencesAsync);
        notifications.MapPut("/preferences", UpdatePreferencesAsync);

        return app;
    }

    /// <summary>Préférences de notification du vendeur (état activé/coupé par catégorie).</summary>
    private static async Task<IResult> GetPreferencesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetNotificationPreferencesQuery(userId), ct)).Match(p => Results.Ok(p));

    /// <summary>Met à jour les catégories de push coupées.</summary>
    private static async Task<IResult> UpdatePreferencesAsync(ClaimsPrincipal user, NotificationPreferencesRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateNotificationPreferencesCommand(userId, request.MutedCategories ?? new List<string>()), ct)).Match(() => Results.NoContent());

    /// <summary>Enregistre le jeton d'appareil du vendeur pour recevoir les push.</summary>
    private static async Task<IResult> RegisterDeviceAsync(ClaimsPrincipal user, DeviceRegisterRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterDeviceTokenCommand(userId, request.Token, request.Platform ?? "unknown"), ct)).Match(() => Results.NoContent());

    /// <summary>Retire un jeton d'appareil (déconnexion / désabonnement push).</summary>
    private static async Task<IResult> UnregisterDeviceAsync(ClaimsPrincipal user, DeviceUnregisterRequest request, ISender sender, CancellationToken ct)
        // L'identifiant est PASSÉ à la commande, pas seulement vérifié : il servait
        // uniquement de test de présence, et le désabonnement s'appliquait ensuite au
        // jeton de n'importe qui.
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UnregisterDeviceTokenCommand(userId, request.Token), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Notifications de l'utilisateur du vendeur (les plus récentes d'abord).
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PAGINÉE (<c>?take=&amp;skip=</c>). CETTE ROUTE N'ACCEPTAIT AUCUN PARAMÈTRE.
    ///
    /// `ListMyNotificationsQuery` laissait `Take` à 50 et il n'y avait pas de décalage :
    /// il n'existait AUCUN moyen d'atteindre la 51ᵉ notification. Un vendeur revenant
    /// après deux semaines avec 130 non lues voyait un badge à 130 — il vient d'un
    /// COUNT, il est juste — au-dessus d'une liste de 50. Pire, le filtre « Non lues »
    /// de la console travaille sur le lot déjà coupé : douze lignes s'affichaient sous
    /// un badge annonçant 130, et le seul recours proposé était « Tout marquer comme
    /// lu », qui les effaçait sans les avoir montrées.
    ///
    /// LA RÉPONSE RESTE UN TABLEAU NU, délibérément. L'app mobile consomme déjà cette
    /// route ; l'envelopper dans un objet paginé l'aurait cassée pour un gain nul —
    /// une page pleine suffit à savoir qu'il faut en demander une autre. Sans
    /// paramètre, le comportement est exactement celui d'avant.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct, int? take = null, int? skip = null)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId, take ?? 50, skip ?? 0), ct))
                .Match(n => Results.Ok(n));

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
