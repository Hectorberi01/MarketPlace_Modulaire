using MediatR;
using System.Security.Claims;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Notifications.Application.Devices;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// Le compte coursier — /driver/me.
///
/// C'est la route que l'application appelle en premier, et c'est elle qui répond à la
/// question « ce compte est-il un coursier ? ». Un 404 « not_a_courier » y est une
/// réponse NORMALE, pas une erreur : c'est ce que voit un acheteur qui installerait
/// l'application coursier, et l'écran d'accueil doit savoir le dire.
/// </summary>
public static class DriverAccountEndpoints
{
    public static IEndpointRouteBuilder MapDriverAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapDriverGroup("/driver", "Driveur · Compte");

        me.MapGet("/me", MeAsync).WithName("DriverMe");

        // ═════════════════════════════════════════════════════════════════════════
        // L'APPAREIL DU COURSIER, POUR QUE L'OFFRE LE RÉVEILLE.
        //
        // Ces deux routes manquaient, et leur absence se voyait au mauvais endroit :
        // l'application livreur ne pouvait pas transmettre son jeton FCM, donc aucune
        // offre ne pouvait l'atteindre téléphone rangé. Le hub SignalR ne couvre que
        // l'application ouverte au premier plan — et une course se propose en
        // vingt-cinq secondes.
        //
        // MÊMES COMMANDES QUE LES DEUX AUTRES SURFACES. Le module Notifications tient
        // déjà les jetons par UTILISATEUR, et un coursier est un utilisateur comme un
        // autre : il n'y a ni table ni cycle de vie à inventer ici. C'est aussi ce qui
        // fait qu'un coursier également acheteur ne porte qu'un seul jeu de jetons.
        // ═════════════════════════════════════════════════════════════════════════
        me.MapPost("/devices", RegisterDeviceAsync).WithName("DriverRegisterDevice");
        me.MapPost("/devices/unregister", UnregisterDeviceAsync).WithName("DriverUnregisterDevice");

        return app;
    }

    /// <summary>Statut du coursier et coordonnées de versement.</summary>
    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user, IDeliveryModuleApi couriers, CancellationToken ct)
    {
        if (DriverBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, ct);
        return courier is null ? Results.NotFound(new { error = "not_a_courier" }) : Results.Ok(courier);
    }

    /// <summary>
    /// Enregistre le jeton push de cet appareil.
    ///
    /// AUCUNE VÉRIFICATION « EST-CE UN COURSIER ? » ICI, et c'est délibéré : le jeton
    /// appartient au COMPTE, pas au rôle. Refuser l'enregistrement à un compte qui
    /// n'est pas encore coursier ferait perdre le jeton exactement pendant la période
    /// d'inscription — celle où l'on a le plus besoin de le joindre.
    /// </summary>
    private static async Task<IResult> RegisterDeviceAsync(
        ClaimsPrincipal user, DriverDeviceRegisterRequest request, ISender sender, CancellationToken ct)
        => DriverBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(
                new RegisterDeviceTokenCommand(userId, request.Token, request.Platform ?? "unknown"), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// Retire le jeton de cet appareil. Appelé à la DÉCONNEXION : sans lui, le
    /// téléphone d'un coursier déconnecté continuerait de recevoir les offres du
    /// compte précédent.
    /// </summary>
    private static async Task<IResult> UnregisterDeviceAsync(
        ClaimsPrincipal user, DriverDeviceUnregisterRequest request, ISender sender, CancellationToken ct)
        => DriverBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UnregisterDeviceTokenCommand(userId, request.Token), ct))
                .Match(() => Results.NoContent());

    public sealed record DriverDeviceRegisterRequest(string Token, string? Platform);

    public sealed record DriverDeviceUnregisterRequest(string Token);
}
