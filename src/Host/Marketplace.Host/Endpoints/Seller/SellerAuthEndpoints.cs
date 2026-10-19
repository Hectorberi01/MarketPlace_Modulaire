using MediatR;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — authentification (/seller/auth/*). Le vendeur se connecte avec
/// son compte Identity (le même que côté acheteur) ; la boutique est résolue
/// ensuite. La création de compte se fait sur la surface acheteur ; l'onboarding
/// boutique vit sous /seller/shop.
/// </summary>
public static class SellerAuthEndpoints
{
    public static IEndpointRouteBuilder MapSellerAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/sellers/auth").WithTags("Vendeur · Auth").RequireRateLimiting("auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        return app;
    }

    /// <summary>Connexion : renvoie access + refresh token.</summary>
    /// <summary>
    /// Connexion à l'app vendeur. Le rôle Seller est exigé DÈS LA PORTE : sans lui,
    /// un acheteur obtenait un jeton valide, l'app s'ouvrait, et il se heurtait à un
    /// 403 sur chaque écran sans comprendre pourquoi.
    /// </summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
    {
        var response = await sender.Send(
            new LoginCommand(request.Email, request.Password, request.MfaCode, SellerRoles),
            ct
        );

        return response.Match(r => Results.Ok(r));

    }

    private static readonly string[] SellerRoles = [SellerBff.SellerRole];

    /// <summary>Rafraîchit l'access token.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
    {

        var response = await sender.Send(new RefreshTokenCommand(request.RefreshToken),ct);
        return response.Match(r => Results.Ok(r));
    }

    /// <summary>Invalide le refresh token.</summary>
    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
    {
        return SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct)).Match(() => Results.NoContent());
    }

    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
}
