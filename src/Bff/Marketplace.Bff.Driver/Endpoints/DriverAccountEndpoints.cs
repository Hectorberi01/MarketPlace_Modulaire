using MediatR;
using System.Security.Claims;
// `Match` sur un `Result` est une extension de l'API, pas une méthode du type : les deux
// autres fichiers d'endpoints de ce BFF l'importent déjà. Sans elle, la compilation
// échoue sur un message qui parle de `Result`, pas d'un using manquant.
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Identity.Application.Users.Commands.AcceptTerms;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Marketing.Application.Legal;
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

        // Photo de profil. Elle vit sur le COMPTE, pas sur le coursier : le même
        // fichier sert à l'acheteur qui suit sa livraison et à la console admin, et un
        // coursier qui est aussi acheteur ne la dépose qu'une fois.
        me.MapPost("/me/avatar", UploadAvatarAsync).WithName("DriverUploadAvatar").DisableAntiforgery();
        me.MapDelete("/me/avatar", RemoveAvatarAsync).WithName("DriverRemoveAvatar");

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
        // ═════════════════════════════════════════════════════════════════════════
        // L'ACCEPTATION DES CONDITIONS, QUI N'EXISTAIT NULLE PART.
        //
        // Les applications acheteur et vendeur ont chacune leur route ; le coursier
        // n'en avait pas, parce qu'il n'avait pas non plus de conditions à accepter.
        // Elle consigne DEUX choses, et il faut les deux : la version sur le compte
        // (c'est elle qui décide si l'écran de consentement se rouvre), et la trace
        // datée au registre versionné (c'est elle qu'on produit en cas de litige).
        // ═════════════════════════════════════════════════════════════════════════
        me.MapPost("/me/accept-terms", AcceptTermsAsync).WithName("DriverAcceptTerms");

        me.MapPost("/devices", RegisterDeviceAsync).WithName("DriverRegisterDevice");
        me.MapPost("/devices/unregister", UnregisterDeviceAsync).WithName("DriverUnregisterDevice");

        return app;
    }

    /// <summary>Statut du coursier et coordonnées de versement.</summary>
    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user, IDeliveryModuleApi couriers, ISender sender, CancellationToken ct)
    {
        if (DriverBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, ct);
        if (courier is null)
        {
            return Results.NotFound(new { error = "not_a_courier" });
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LA JOINTURE SE FAIT ICI, ET NULLE PART AILLEURS.
        //
        // La photo est sur le compte (module Identity), le coursier est dans Delivery,
        // et les deux modules ne se lisent pas entre eux. Le BFF est le seul endroit
        // qui a les deux sous la main : c'est donc lui qui assemble.
        //
        // La réponse garde EXACTEMENT les champs de CourierSummary, avec un champ de
        // plus. Une application déjà publiée continue de la lire sans rien changer.
        // ═════════════════════════════════════════════════════════════════════════
        var compte = await sender.Send(new GetUserQuery(userId), ct);

        return Results.Ok(new DriverMeView(
            courier.Id, courier.UserId, courier.DisplayName, courier.Phone, courier.Status,
            courier.CanBePaid, courier.ExternalRef, courier.Payout, courier.CreatedAtUtc,
            compte.IsSuccess ? compte.Value.AvatarUrl : null,
            compte.IsSuccess ? compte.Value.AcceptedTermsVersion : null));
    }

    /// <summary>
    /// Consigne l'acceptation des conditions par le coursier.
    ///
    /// LA VERSION VIENT DE L'APPLICATION, et c'est celle du texte qu'elle A AFFICHÉ.
    /// Le serveur ne la remplace pas par « la dernière publiée » : le coursier a
    /// accepté ce qu'il a lu, et si son application montrait encore une rédaction
    /// précédente, c'est celle-là qui l'engage.
    ///
    /// LA TRACE PEUT ÉCHOUER SANS ANNULER LE CONSENTEMENT. `recorded: false` signifie
    /// que la version présentée ne correspond à aucune version publiée du CMS — le cas
    /// normal tant qu'aucun document n'y est publié, et le cas d'une application
    /// ancienne qui présente sa copie embarquée. Le consentement, lui, est déjà
    /// consigné sur le compte. C'est un signal pour la plateforme, pas une sanction
    /// pour le coursier, et il ne le bloque jamais.
    /// </summary>
    private static async Task<IResult> AcceptTermsAsync(
        ClaimsPrincipal user, DriverAcceptTermsRequest request, ISender sender, CancellationToken ct)
    {
        if (DriverBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var compte = await sender.Send(new AcceptTermsCommand(userId, request.Version), ct);
        if (compte.IsFailure)
        {
            return compte.Match(() => Results.NoContent());
        }

        var trace = await sender.Send(
            new RecordLegalAcceptanceCommand(userId, "legal_terms_driver", request.Version, "consent_gate_driver"), ct);

        return Results.Ok(new { version = request.Version, recorded = trace.IsSuccess && trace.Value });
    }

    /// <summary>Téléverse ou remplace la photo de profil du coursier.</summary>
    private static async Task<IResult> UploadAvatarAsync(
        ClaimsPrincipal user, IFormFile file, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => DriverBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.SetAsync(userId, file, sender, mediaStorage, ct);

    /// <summary>Retire la photo de profil du coursier.</summary>
    private static async Task<IResult> RemoveAvatarAsync(
        ClaimsPrincipal user, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => DriverBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.RemoveAsync(userId, sender, mediaStorage, ct);

    /// <summary>
    /// CourierSummary, plus la photo du compte. Voir la jointure dans MeAsync.
    /// </summary>
    public sealed record DriverMeView(
        Guid Id,
        Guid UserId,
        string DisplayName,
        string Phone,
        string Status,
        bool CanBePaid,
        string? ExternalRef,
        CourierPayoutSummary? Payout,
        DateTime CreatedAtUtc,
        string? AvatarUrl,

        /// La version des conditions déjà acceptée, ou nulle. C'est ce que
        /// l'application compare au document servi pour savoir si elle doit rouvrir
        /// l'écran de consentement — sans ce champ, elle ne peut que supposer.
        string? AcceptedTermsVersion);

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

    public sealed record DriverAcceptTermsRequest(string Version);

    public sealed record DriverDeviceRegisterRequest(string Token, string? Platform);

    public sealed record DriverDeviceUnregisterRequest(string Token);
}
