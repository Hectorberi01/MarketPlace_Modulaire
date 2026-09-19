using MediatR;
using Marketplace.Modules.Marketing.Application.Legal;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Identity.Application.PaymentMethods;
using Marketplace.Modules.Identity.Application.Users.Commands.AcceptTerms;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Identity.Application.Users.Commands.DeleteAccount;
using Marketplace.Modules.Identity.Application.Users.Commands.Mfa;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Notifications.Application.Devices;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;
using Marketplace.Modules.Notifications.Application.Notifications.Preferences;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Wishlist.Application.Wishlists;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>Corps d'enregistrement d'un appareil push (FCM) — app cliente.</summary>
public sealed record ClientDeviceRegisterRequest(string Token, string? Platform);

/// <summary>Corps de désenregistrement d'un appareil push — app cliente.</summary>
public sealed record ClientDeviceUnregisterRequest(string Token);

/// <summary>
/// Suppression du compte. Le mot de passe est EXIGÉ : l'action est irréversible, et un
/// téléphone déverrouillé laissé sans surveillance ne doit pas suffire à l'exécuter.
/// </summary>
public sealed record DeleteAccountRequest(string Password);

/// <summary>Corps d'une action MFA nécessitant un code TOTP (confirmation / désactivation).</summary>
public sealed record MfaCodeRequest(string Code);

/// <summary>Catégories de push que l'acheteur souhaite couper (opt-out).</summary>
public sealed record ClientNotificationPreferencesRequest(List<string>? MutedCategories);

/// <summary>BFF Client — compte, favoris et notifications (scopés au jeton).</summary>
public static class ClientAccountEndpoints
{
    public static IEndpointRouteBuilder MapClientAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/client").WithTags("Client · Compte").RequireAuthorization();

        mobile.MapGet("/account/profile", ProfileAsync);
        mobile.MapPut("/account/profile", UpdateProfileAsync);

        // Photo de profil (multipart, champ « file »). L'URL remonte dans le profil,
        // sur UserSummary.AvatarUrl — il n'y a donc pas de route de lecture dédiée.
        mobile.MapPost("/account/avatar", UploadAvatarAsync).DisableAntiforgery();
        mobile.MapDelete("/account/avatar", RemoveAvatarAsync);

        // Consentement aux conditions générales. La version acceptée remonte déjà
        // dans le profil (UserSummary) : l'app compare, et n'affiche l'écran que
        // si le texte a changé.
        mobile.MapPost("/account/accept-terms", AcceptTermsAsync);

        // SUPPRESSION DU COMPTE — exigée par Apple (Guideline 5.1.1(v)) dès lors que
        // l'application permet de créer un compte. Sans ce parcours, l'app cliente est
        // REJETÉE à la soumission.
        mobile.MapDelete("/account", DeleteAccountAsync);

        mobile.MapGet("/wishlist", WishlistAsync);
        mobile.MapPost("/wishlist/items", AddWishlistAsync);
        mobile.MapPut("/wishlist/items/{productId:guid}/alerts", SetWishlistAlertsAsync);
        mobile.MapDelete("/wishlist/items/{productId:guid}", RemoveWishlistAsync);

        mobile.MapGet("/notifications", NotificationsAsync);
        mobile.MapPatch("/notifications/{id:guid}/read", MarkReadAsync);
        mobile.MapDelete("/notifications/{id:guid}", DeleteNotificationAsync);

        // Moyens de paiement enregistrés (Mobile Money + cartes masquées).
        mobile.MapGet("/account/payment-methods", ListPaymentMethodsAsync);
        mobile.MapPost("/account/payment-methods", AddPaymentMethodAsync);
        mobile.MapPut("/account/payment-methods/{id:guid}", UpdatePaymentMethodAsync);
        mobile.MapDelete("/account/payment-methods/{id:guid}", DeletePaymentMethodAsync);
        mobile.MapPut("/account/payment-methods/{id:guid}/default", SetDefaultPaymentMethodAsync);

        // Double authentification (TOTP) — activer / confirmer / désactiver.
        mobile.MapPost("/account/me/mfa/setup", MfaSetupAsync);
        mobile.MapPost("/account/me/mfa/confirm", MfaConfirmAsync);
        mobile.MapPost("/account/me/mfa/disable", MfaDisableAsync);

        // Préférences de notification (catégories de push coupées) + enregistrement
        // de token push (FCM/APNs). Branché sur le module Notifications, comme le BFF
        // vendeur : opt-out par catégorie, l'acheteur reste notifié par défaut.
        mobile.MapGet("/notifications/preferences", GetNotificationPreferencesAsync);
        mobile.MapPut("/notifications/preferences", UpdateNotificationPreferencesAsync);
        mobile.MapPost("/devices", RegisterDeviceAsync);
        mobile.MapPost("/devices/unregister", UnregisterDeviceAsync);

        return app;
    }

    /// <summary>Démarre la configuration MFA : renvoie le secret TOTP + l'URI otpauth (QR).</summary>
    private static async Task<IResult> MfaSetupAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new BeginMfaSetupCommand(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Confirme l'activation MFA avec un premier code TOTP.</summary>
    private static async Task<IResult> MfaConfirmAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ConfirmMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    /// <summary>Désactive la MFA après vérification d'un code TOTP valide.</summary>
    private static async Task<IResult> MfaDisableAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DisableMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// SUPPRESSION DU COMPTE (Apple Guideline 5.1.1(v)).
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA SUPPRESSION EST REFUSÉE SI DES AFFAIRES SONT EN COURS — ET C'EST UN SERVICE.
    ///
    /// Le réflexe serait d'obéir sans discuter. Ce serait une faute :
    ///
    ///  • une COMMANDE EN COURS rattachée à un compte anonymisé devient impossible à
    ///    livrer et impossible à rembourser. On aurait exaucé le vœu de l'acheteur en
    ///    lui faisant perdre son argent ;
    ///  • un LITIGE OUVERT sans plaignant ne peut plus être tranché — au bénéfice de
    ///    personne ;
    ///  • une BOUTIQUE VENDEUR implique un solde, des versements, des obligations
    ///    fiscales. Cela ne se supprime pas depuis un écran de téléphone.
    ///
    /// On refuse donc, EN DISANT PRÉCISÉMENT CE QUI BLOQUE. Un refus sans explication
    /// pousse l'utilisateur à croire au bug — et à écrire au support, ce qui vous coûte
    /// plus cher que le refus lui-même.
    ///
    /// Apple accepte parfaitement ce garde-fou : l'exigence est qu'un chemin de
    /// suppression EXISTE, pas qu'il ignore les obligations légales et commerciales.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> DeleteAccountAsync(
        ClaimsPrincipal user,
        // DELETE n'infère PAS de corps en minimal-API : sans [FromBody] explicite,
        // le démarrage échoue (« method does not allow inferred body parameters »).
        [FromBody] DeleteAccountRequest request,
        ISender sender,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        // 1. VENDEUR ? Un compte marchand porte un solde et des obligations : il ne se
        //    supprime pas depuis l'app cliente.
        var seller = await sellers.GetSellerByUserIdAsync(userId, ct);
        if (seller is not null)
        {
            return Results.Problem(
                title: "identity.account.seller_must_contact_support",
                detail: "Votre compte est rattaché à une boutique. Pour des raisons comptables et fiscales, "
                      + "la suppression d'un compte vendeur passe par notre support : support@hba-express.com.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // 2. COMMANDE EN COURS ? Tout ce qui n'est ni livré, ni annulé, ni échoué.
        var orders = await sender.Send(new ListMyOrdersQuery(userId), ct);
        if (orders.IsSuccess)
        {
            var ongoing = orders.Value
                .Count(o => !string.Equals(o.Status, "Delivered", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(o.Status, "Failed", StringComparison.OrdinalIgnoreCase));

            if (ongoing > 0)
            {
                return Results.Problem(
                    title: "identity.account.orders_in_progress",
                    detail: ongoing == 1
                        ? "Vous avez une commande en cours. Attendez sa livraison (ou son annulation) avant de supprimer votre compte : "
                          + "sans compte, nous ne pourrions plus vous livrer ni vous rembourser."
                        : $"Vous avez {ongoing} commandes en cours. Attendez leur livraison (ou leur annulation) avant de supprimer votre compte : "
                          + "sans compte, nous ne pourrions plus vous livrer ni vous rembourser.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        // 3. LITIGE OUVERT ? Un litige sans plaignant ne se tranche pas.
        var disputes = await sender.Send(new ListMyDisputesQuery(userId), ct);
        if (disputes.IsSuccess)
        {
            var open = disputes.Value.Count(d =>
                string.Equals(d.Status, "Open", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Status, "UnderReview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Status, "Escalated", StringComparison.OrdinalIgnoreCase));

            if (open > 0)
            {
                return Results.Problem(
                    title: "identity.account.disputes_open",
                    detail: "Un litige vous concernant est encore ouvert. Il doit être résolu avant la suppression de votre compte — "
                          + "sans quoi nous ne pourrions plus vous verser un éventuel remboursement.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        return (await sender.Send(new DeleteAccountCommand(userId, request.Password), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>Enregistre le jeton d'appareil (FCM) de l'acheteur pour les push.</summary>
    private static async Task<IResult> RegisterDeviceAsync(ClaimsPrincipal user, ClientDeviceRegisterRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterDeviceTokenCommand(userId, request.Token, request.Platform ?? "unknown"), ct)).Match(() => Results.NoContent());

    /// <summary>Préférences de notification de l'acheteur (catégories + état activé/coupé).</summary>
    private static async Task<IResult> GetNotificationPreferencesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetNotificationPreferencesQuery(userId), ct)).Match(p => Results.Ok(p));

    /// <summary>Met à jour les catégories de push coupées (opt-out).</summary>
    private static async Task<IResult> UpdateNotificationPreferencesAsync(
        ClaimsPrincipal user, ClientNotificationPreferencesRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateNotificationPreferencesCommand(userId, request.MutedCategories ?? new List<string>()), ct))
                .Match(() => Results.NoContent());

    /// <summary>Retire un jeton d'appareil (déconnexion / désabonnement push).</summary>
    private static async Task<IResult> UnregisterDeviceAsync(ClaimsPrincipal user, ClientDeviceUnregisterRequest request, ISender sender, CancellationToken ct)
        // Même correction que sur la surface vendeur : l'identifiant borne l'opération
        // à son propriétaire, il ne sert plus seulement de test de présence.
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UnregisterDeviceTokenCommand(userId, request.Token), ct)).Match(() => Results.NoContent());

    /// <summary>Profil utilisateur.</summary>
    private static async Task<IResult> ProfileAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserQuery(userId), ct)).Match(u => Results.Ok(u));

    /// <summary>
    /// Enregistre l'acceptation d'une version des conditions générales.
    ///
    /// La version vient du CLIENT : c'est le texte qu'il avait sous les yeux. Faire
    /// consigner par le serveur « la version courante » sans savoir laquelle a été
    /// affichée reviendrait à faire signer un document qu'on n'a pas montré.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// DEUX ÉCRITURES, ET LA RÉPONSE DIT LAQUELLE A PRIS.
    ///
    /// 1. L'ANCIENNE : deux colonnes sur le compte. Une seule case pour toute la
    ///    plateforme — accepter un second document écrasait la trace du premier. Elle
    ///    reste écrite parce que les portillons de consentement déjà déployés la
    ///    lisent ; la retirer maintenant les ferait tous redemander l'accord.
    ///
    /// 2. LA NOUVELLE : une ligne par couple (personne, version), dans le module
    ///    Contenu. C'est elle qui répond à « qu'avait cette personne sous les yeux ».
    ///
    /// `recorded` dit si la seconde a pu s'écrire. FAUX signifie que la version
    /// présentée ne correspond à aucune version publiée — le cas normal tant que le
    /// document n'est pas dans le CMS : l'application affiche alors sa copie
    /// embarquée. On ne fabrique PAS de version fantôme pour faire réussir l'appel :
    /// une preuve qui pointe vers un texte que personne ne peut relire est pire
    /// qu'une absence de preuve, parce qu'elle en a l'apparence.
    ///
    /// Un `false` ne bloque JAMAIS la personne. C'est un signal pour la plateforme,
    /// pas une sanction pour l'utilisateur.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> AcceptTermsAsync(ClaimsPrincipal user, AcceptTermsRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var legacy = await sender.Send(new AcceptTermsCommand(userId, request.Version), ct);
        if (legacy.IsFailure)
        {
            return legacy.Match(() => Results.NoContent());
        }

        var trace = await sender.Send(
            new RecordLegalAcceptanceCommand(userId, "legal_terms_customer", request.Version, "consent_gate_client"), ct);

        // L'échec de la trace n'annule pas le consentement : il est déjà consigné sur le
        // compte. On le rend visible plutôt que de le taire.
        return Results.Ok(new { version = request.Version, recorded = trace.IsSuccess && trace.Value });
    }

    /// <summary>Met à jour le profil.</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Téléverse ou remplace la photo de profil de l'acheteur.</summary>
    private static async Task<IResult> UploadAvatarAsync(
        ClaimsPrincipal user, IFormFile file, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.SetAsync(userId, file, sender, mediaStorage, ct);

    /// <summary>Retire la photo de profil de l'acheteur.</summary>
    private static async Task<IResult> RemoveAvatarAsync(
        ClaimsPrincipal user, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.RemoveAsync(userId, sender, mediaStorage, ct);

    /// <summary>Liste d'envies.</summary>
    private static async Task<IResult> WishlistAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyWishlistQuery(userId), ct)).Match(w => Results.Ok(w));

    /// <summary>Ajoute un produit aux favoris.</summary>
    private static async Task<IResult> AddWishlistAsync(ClaimsPrincipal user, WishlistRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AddToWishlistCommand(userId, request.ProductId, request.OfferId, request.PriceAlert, request.StockAlert), ct)).Match(() => Results.NoContent());

    /// <summary>Active/désactive les alertes (baisse de prix, retour en stock) d'un favori.</summary>
    private static async Task<IResult> SetWishlistAlertsAsync(
        ClaimsPrincipal user, Guid productId, WishlistAlertsRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetWishlistAlertsCommand(userId, productId, request.PriceAlert, request.StockAlert), ct))
                .Match(() => Results.NoContent());

    /// <summary>Retire des favoris.</summary>
    private static async Task<IResult> RemoveWishlistAsync(ClaimsPrincipal user, Guid productId, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFromWishlistCommand(userId, productId), ct)).Match(() => Results.NoContent());

    /// <summary>Notifications in-app.</summary>
    private static async Task<IResult> NotificationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId), ct)).Match(n => Results.Ok(n));

    /// <summary>Marque une notification comme lue.</summary>
    private static async Task<IResult> MarkReadAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkNotificationReadCommand(id, userId), ct)).Match(() => Results.NoContent());

    /// <summary>Supprime une notification de l'utilisateur (balayage vers la gauche).</summary>
    private static async Task<IResult> DeleteNotificationAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeleteOwnNotificationCommand(id, userId), ct)).Match(() => Results.NoContent());

    /// <summary>Liste les moyens de paiement de l'utilisateur (défaut en tête).</summary>
    private static async Task<IResult> ListPaymentMethodsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListPaymentMethodsQuery(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Enregistre un moyen de paiement (Mobile Money ou carte).</summary>
    private static async Task<IResult> AddPaymentMethodAsync(ClaimsPrincipal user, PaymentMethodRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddPaymentMethodCommand(
            userId, request.Type, request.Label, request.Provider, request.Msisdn,
            request.CardNumber, request.ExpiryMonth, request.ExpiryYear, request.HolderName,
            request.MakeDefault), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Met à jour un moyen de paiement enregistré (scopé au jeton).</summary>
    private static async Task<IResult> UpdatePaymentMethodAsync(ClaimsPrincipal user, Guid id, UpdatePaymentMethodRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdatePaymentMethodCommand(
            userId, id, request.Label, request.Provider, request.Msisdn,
            request.ExpiryMonth, request.ExpiryYear, request.HolderName, request.MakeDefault), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime un moyen de paiement.</summary>
    private static async Task<IResult> DeletePaymentMethodAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeletePaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    /// <summary>Définit le moyen de paiement par défaut.</summary>
    private static async Task<IResult> SetDefaultPaymentMethodAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetDefaultPaymentMethodCommand(userId, id), ct)).Match(() => Results.NoContent());

    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record AcceptTermsRequest(string Version);
    public sealed record WishlistRequest(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert);
    public sealed record WishlistAlertsRequest(bool PriceAlert, bool StockAlert);

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

    public sealed record UpdatePaymentMethodRequest(
        string? Label,
        string? Provider,
        string? Msisdn,
        int? ExpiryMonth,
        int? ExpiryYear,
        string? HolderName,
        bool MakeDefault);
}
