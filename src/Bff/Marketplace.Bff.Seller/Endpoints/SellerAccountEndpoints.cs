using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ChangePassword;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
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
        var me = app.MapGroup("/seller").WithTags("Vendeur · Compte").RequireAuthorization();

        // Identité consolidée : profil + boutique en un appel.
        me.MapGet("/me", MeAsync);

        var account = app.MapGroup("/seller/account").WithTags("Vendeur · Compte").RequireAuthorization();
        account.MapGet("/me", ProfileAsync);
        account.MapPut("/me", UpdateProfileAsync);
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

    /// <summary>Profil Identity du compte connecté.</summary>
    private static async Task<IResult> ProfileAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserQuery(userId), ct)).Match(u => Results.Ok(u));

    /// <summary>Met à jour le profil du compte (nom, téléphone).</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct)).Match(() => Results.NoContent());

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
    public sealed record ProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
}
