using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — authentification (/admin/auth/*). L'administrateur se connecte avec
/// son compte Identity ; le rôle Admin (porté par le jeton) conditionne l'accès
/// aux autres routes /admin.
/// </summary>
public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/admin/auth").WithTags("Admin · Auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        return app;
    }

    /// <summary>Connexion : renvoie access + refresh token.</summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct)).Match(r => Results.Ok(r));

    /// <summary>Rafraîchit l'access token.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct)).Match(r => Results.Ok(r));

    /// <summary>Invalide le refresh token.</summary>
    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct)).Match(() => Results.NoContent());

    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
}
