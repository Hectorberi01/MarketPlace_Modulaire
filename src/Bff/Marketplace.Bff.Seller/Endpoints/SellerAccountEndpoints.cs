using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ChangePassword;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Commands.AcceptTerms;
using Marketplace.Modules.Identity.Application.Users.Commands.Mfa;
using Marketplace.Modules.Identity.Application.Users.Commands.DeleteAccount;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RequestSellerClosure;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RequestSellerReactivation;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.DeleteSeller;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — identité du compte connecté. « /seller/me » renvoie en un appel
/// le profil Identity du vendeur ET sa boutique. Les autres routes gèrent le
/// compte (profil, mot de passe, MFA). L'identité provient toujours du jeton.
/// </summary>
public static class SellerAccountEndpoints
{
    public static IEndpointRouteBuilder MapSellerAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapSellerGroup("/seller", "Vendeur · Compte");

        // Identité consolidée : profil + boutique en un appel.
        me.MapGet("/me", MeAsync);

        var account = app.MapSellerGroup("/seller/account", "Vendeur · Compte");
        account.MapGet("/me", ProfileAsync);
        account.MapPut("/me", UpdateProfileAsync);
        account.MapPost("/me/accept-terms", AcceptTermsAsync);

        // Photo de profil du VENDEUR, à ne pas confondre avec le logo de sa boutique :
        // l'une est la personne, l'autre l'enseigne. Les deux coexistent sur cet écran.
        account.MapPost("/me/avatar", UploadAvatarAsync).DisableAntiforgery();
        account.MapDelete("/me/avatar", RemoveAvatarAsync);
        account.MapPost("/me/change-password", ChangePasswordAsync);
        account.MapPost("/me/mfa/setup", MfaSetupAsync);
        account.MapPost("/me/mfa/confirm", MfaConfirmAsync);
        account.MapPost("/me/mfa/disable", MfaDisableAsync);

        // Cycle de vie du compte vendeur (suppression partielle / réactivation).
        account.MapPost("/me/close", CloseAccountAsync);
        account.MapPost("/me/request-reactivation", RequestReactivationAsync);

        // Suppression DÉFINITIVE en libre-service (exigence App Store 5.1.1(v)) :
        // l'utilisateur initie ET confirme depuis l'app, sans passer par un admin.
        account.MapDelete("/me", DeleteAccountAsync);

        return app;
    }

    /// <summary>
    /// Suppression DÉFINITIVE du compte vendeur, en libre-service.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// Pourquoi cet endpoint existe : Apple (App Store Review 5.1.1(v)) exige qu'une
    /// app permettant de CRÉER un compte permette aussi de le SUPPRIMER — réellement,
    /// pas seulement le suspendre — sans devoir contacter le service client.
    ///
    /// Ce que l'on fait :
    ///   1. Garde-fou buyer-friendly : on refuse si des commandes sont EN COURS (le
    ///      vendeur doit d'abord les livrer/annuler). C'est auto-résolvable, jamais un
    ///      « contactez le support » — Apple accepte les garde-fous, pas les murs.
    ///   2. Mot de passe exigé (via DeleteAccountCommand) : l'action est irréversible.
    ///   3. Anonymisation de l'identité (RGPD) + purge de la boutique et des produits.
    ///
    /// Les enregistrements exigés par la loi (factures, versements) restent conservés
    /// côté serveur, détachés de toute donnée personnelle — ce qu'Apple autorise.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> DeleteAccountAsync(
        ClaimsPrincipal user,
        // DELETE n'infère pas de corps en minimal-API : [FromBody] est obligatoire,
        // sinon le démarrage échoue.
        [FromBody] DeleteAccountRequest request,
        ISender sender,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        // La boutique peut être absente (compte créé, onboarding non terminé) : dans
        // ce cas il n'y a que l'identité à supprimer.
        var seller = await sellers.GetSellerByUserIdAsync(userId, ct);

        // Garde-fou : commandes en cours (ni livrées, ni annulées). Supprimer un
        // vendeur au milieu d'une expédition laisserait l'acheteur sans interlocuteur.
        if (seller is not null)
        {
            var ordersResult = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
            if (ordersResult.IsSuccess)
            {
                var ongoing = ordersResult.Value.Count(o =>
                    !string.Equals(o.Status, "Delivered", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(o.Status, "Failed", StringComparison.OrdinalIgnoreCase));

                if (ongoing > 0)
                {
                    return Results.Problem(
                        title: "sellers.account.orders_in_progress",
                        detail: ongoing == 1
                            ? "Vous avez une commande en cours. Livrez-la (ou annulez-la) avant de supprimer votre compte : "
                              + "sans compte, l'acheteur n'aurait plus d'interlocuteur."
                            : $"Vous avez {ongoing} commandes en cours. Livrez-les (ou annulez-les) avant de supprimer votre compte : "
                              + "sans compte, les acheteurs n'auraient plus d'interlocuteur.",
                        statusCode: StatusCodes.Status409Conflict);
                }
            }
        }

        // Mot de passe vérifié + identité anonymisée. Si le mot de passe est faux,
        // rien de destructeur n'a lieu (l'échec sort ici).
        var deleted = await sender.Send(new DeleteAccountCommand(userId, request.Password), ct);
        if (deleted.IsFailure)
        {
            return deleted.Match(() => Results.NoContent());
        }

        // ─────────────────────────────────────────────────────────────────────────
        // L'ÉCHEC DE LA PURGE DE BOUTIQUE DOIT REMONTER.
        //
        // Le résultat de `DeleteSellerCommand` était ignoré, et `204` renvoyé quoi
        // qu'il arrive. En cas d'échec, la boutique et ses produits restaient EN VENTE
        // derrière un compte déjà anonymisé, donc injoignable — pendant que
        // l'utilisateur recevait la confirmation que tout était supprimé.
        //
        // C'est aussi une exigence App Store (5.1.1(v)) : la suppression annoncée doit
        // être la suppression effectuée.
        //
        // On renvoie 500 plutôt que 204 : l'identité EST anonymisée, l'opération n'est
        // donc pas annulable, mais l'utilisateur doit savoir qu'elle est incomplète et
        // l'exploitant doit voir l'incident. Le message ne promet rien qu'on ne tienne.
        // ─────────────────────────────────────────────────────────────────────────
        if (seller is not null)
        {
            var shopDeleted = await sender.Send(new DeleteSellerCommand(seller.Id), ct);
            if (shopDeleted.IsFailure)
            {
                return Results.Problem(
                    title: "sellers.account.shop_deletion_failed",
                    detail: "Votre compte a été anonymisé, mais votre boutique n'a pas pu être supprimée. "
                          + "Elle a été signalée à notre équipe, qui la retirera. Contactez le support si "
                          + "elle reste visible.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Fermeture du compte demandée par le vendeur (suppression partielle) : bascule
    /// le compte en « fermé » et retire ses produits de la vente. La suppression
    /// définitive relève de l'admin ; le vendeur garde un accès restreint.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// AUCUN CONTRÔLE DE STATUT SUR TOUT CE FICHIER, ET C'EST LE POINT.
    ///
    /// Les écritures catalogue passent maintenant par `ResolveSellingSellerAsync`
    /// (voir SellerRights). LA GESTION DU COMPTE, ELLE, N'Y PASSERA JAMAIS : fermer,
    /// demander sa réactivation, changer son mot de passe, supprimer son compte sont
    /// les gestes par lesquels un vendeur SORT de son état — pas ceux par lesquels il
    /// en profite.
    ///
    /// Les brancher sur le statut fabriquerait un cul-de-sac parfait : un vendeur
    /// suspendu ne pourrait plus rien faire, pas même demander sa réactivation ni
    /// sécuriser son compte, et il ne resterait que le support pour l'en sortir. Une
    /// sanction doit avoir une porte de sortie, sinon ce n'est plus une sanction.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> CloseAccountAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return (await sender.Send(new RequestSellerClosureCommand(seller.Id), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>
    /// Le vendeur (compte fermé) demande la réactivation : soumise à validation admin.
    ///
    /// C'est LA route qu'il ne faut surtout pas verrouiller sur le statut : elle
    /// n'existe que pour être appelée par un vendeur dont le statut interdit déjà tout
    /// le reste. Voir le pavé sur `CloseAccountAsync`.
    /// </summary>
    private static async Task<IResult> RequestReactivationAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return (await sender.Send(new RequestSellerReactivationCommand(seller.Id), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>Identité du vendeur connecté : compte Identity + boutique (null si pas encore onboardé).</summary>
    private static async Task<IResult> MeAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var account = await sender.Send(new GetUserQuery(userId), ct);
        if (account.IsFailure)
        {
            return account.Match(a => Results.Ok(a));
        }

        // Résolution des noms de rôles (le compte ne porte que les RoleIds).
        var roles = await sender.Send(new ListRolesQuery(), ct);
        var roleNames = roles.IsSuccess
            ? account.Value.RoleIds
                .Select(id => roles.Value.FirstOrDefault(r => r.Id == id)?.Name)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList()
            : new List<string>();

        var shop = await sellers.GetSellerByUserIdAsync(userId, ct);
        return Results.Ok(new SellerMe(account.Value, roleNames, shop));
    }

    /// <summary>
    /// Profil du compte connecté, ENRICHI de sa boutique.
    ///
    /// Cette route renvoyait un <c>UserSummary</c> brut — qui ne contient pas le nom
    /// de la boutique. Les deux fronts (app Flutter et console web) lisaient pourtant
    /// un champ <c>shopName</c> avec un repli « Ma boutique » : ce repli était donc
    /// TOUJOURS affiché, et aucun vendeur n'a jamais vu le nom de sa propre boutique
    /// sur son profil. Le vrai nom n'existait que sur <c>/seller/me</c>, que personne
    /// n'appelait.
    ///
    /// L'enrichissement est ADDITIF (mêmes champs qu'avant, plus trois) : les clients
    /// existants continuent de fonctionner sans être recompilés.
    /// </summary>
    private static async Task<IResult> ProfileAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var account = await sender.Send(new GetUserQuery(userId), ct);
        if (account.IsFailure)
        {
            return account.Match(u => Results.Ok(u));
        }

        var u = account.Value;

        // La boutique peut être absente (compte créé, onboarding non terminé) : on ne
        // fabrique pas de nom, on laisse le champ nul et le client décide quoi dire.
        var shop = await sellers.GetSellerByUserIdAsync(userId, ct);

        return Results.Ok(new SellerAccountMe(
            u.Id, u.FirstName, u.LastName, u.Email, u.PhoneNumber,
            u.Status, u.EmailVerified, u.MfaEnabled, u.RoleIds,
            shop?.ShopName, shop?.LogoUrl, shop?.KybStatus,
            u.AcceptedTermsVersion, u.AcceptedTermsOnUtc,
            u.AvatarUrl));
    }

    /// <summary>Met à jour le profil du compte (nom, téléphone).</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Enregistre l'acceptation d'une version des conditions générales.
    ///
    /// La version vient du CLIENT : c'est le texte qu'il avait effectivement sous
    /// les yeux. Faire consigner par le serveur « la version courante » sans savoir
    /// laquelle a été affichée reviendrait à faire signer un document qu'on n'a pas
    /// montré — et à ne rien pouvoir prouver le jour d'un litige.
    /// </summary>
    private static async Task<IResult> AcceptTermsAsync(ClaimsPrincipal user, AcceptTermsRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AcceptTermsCommand(userId, request.Version), ct)).Match(() => Results.NoContent());

    /// <summary>Change le mot de passe du compte.</summary>
    private static async Task<IResult> ChangePasswordAsync(ClaimsPrincipal user, ChangePasswordRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword), ct)).Match(() => Results.NoContent());

    /// <summary>Démarre la configuration MFA (renvoie le secret / QR).</summary>
    private static async Task<IResult> MfaSetupAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new BeginMfaSetupCommand(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Confirme l'activation MFA avec un code TOTP.</summary>
    private static async Task<IResult> MfaConfirmAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ConfirmMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    /// <summary>Désactive le MFA avec un code TOTP.</summary>
    private static async Task<IResult> MfaDisableAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DisableMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    public sealed record SellerMe(UserSummary Account, IReadOnlyList<string> Roles, SellerSummary? Shop);

    /// <summary>
    /// Profil du compte, à plat, avec le strict nécessaire de la boutique.
    ///
    /// À plat volontairement : c'est la forme qu'attendaient déjà les clients. Les
    /// imbriquer dans un objet « shop » aurait cassé leur désérialisation pour le seul
    /// plaisir d'une hiérarchie plus jolie.
    /// </summary>
    /// <summary>Téléverse ou remplace la photo de profil du vendeur.</summary>
    private static async Task<IResult> UploadAvatarAsync(
        ClaimsPrincipal user, IFormFile file, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.SetAsync(userId, file, sender, mediaStorage, ct);

    /// <summary>Retire la photo de profil du vendeur.</summary>
    private static async Task<IResult> RemoveAvatarAsync(
        ClaimsPrincipal user, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : await AvatarUpload.RemoveAsync(userId, sender, mediaStorage, ct);

    public sealed record SellerAccountMe(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        string PhoneNumber,
        string Status,
        bool EmailVerified,
        bool MfaEnabled,
        IReadOnlyList<Guid> RoleIds,
        string? ShopName,
        string? LogoUrl,
        string? KybStatus,
        // Sans ces deux champs, l'app ne peut pas savoir s'il faut redemander
        // l'accord : elle afficherait l'écran de consentement à chaque lancement,
        // ou — pire — ne l'afficherait jamais.
        string? AcceptedTermsVersion,
        DateTime? AcceptedTermsOnUtc,
        // Photo du vendeur lui-même. LogoUrl, plus haut, est celle de sa boutique.
        string? AvatarUrl);
    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
    public sealed record AcceptTermsRequest(string Version);

    /// <summary>Suppression définitive : le mot de passe confirme que c'est bien le titulaire.</summary>
    public sealed record DeleteAccountRequest(string Password);
}
