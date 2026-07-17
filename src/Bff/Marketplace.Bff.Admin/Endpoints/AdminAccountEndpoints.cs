using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — compte de l'administrateur CONNECTÉ (« /admin/account »).
///
/// L'identité provient TOUJOURS du jeton (jamais d'un id passé par le client) : un
/// administrateur ne peut éditer que sa propre fiche via ces routes. La mise à jour
/// se limite au profil « personnel » (prénom, nom, téléphone) ; l'e-mail (re-vérification)
/// et le mot de passe (forgot/reset, MFA) suivent leurs propres flux dédiés.
/// </summary>
public static class AdminAccountEndpoints
{
    public static IEndpointRouteBuilder MapAdminAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapAdminGroup("/admin/account", "Admin · Compte");

        account.MapGet("/", GetMyAccountAsync);
        account.MapPut("/profile", UpdateMyProfileAsync);

        return app;
    }

    /// <summary>Fiche du compte administrateur connecté (pré-remplissage du formulaire de profil).</summary>
    private static async Task<IResult> GetMyAccountAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserQuery(userId), ct)).Match(u => Results.Ok(u));

    /// <summary>Met à jour le profil personnel du compte connecté (prénom, nom, téléphone).</summary>
    private static async Task<IResult> UpdateMyProfileAsync(ClaimsPrincipal user, AdminProfileRequest request, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(
                new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber ?? string.Empty),
                ct)).Match(() => Results.NoContent());

    public sealed record AdminProfileRequest(string FirstName, string LastName, string? PhoneNumber);
}
