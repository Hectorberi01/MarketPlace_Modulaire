using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ChangePassword;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Commands.AcceptTerms;
using Marketplace.Modules.Identity.Application.Users.Commands.Mfa;

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
        account.MapPost("/me/change-password", ChangePasswordAsync);
        account.MapPost("/me/mfa/setup", MfaSetupAsync);
        account.MapPost("/me/mfa/confirm", MfaConfirmAsync);
        account.MapPost("/me/mfa/disable", MfaDisableAsync);

        return app;
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
            u.AcceptedTermsVersion, u.AcceptedTermsOnUtc));
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
        DateTime? AcceptedTermsOnUtc);
    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
    public sealed record AcceptTermsRequest(string Version);
}
