using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;

namespace Marketplace.Bff.Seller.Endpoints;

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
        var auth = app.MapGroup("/seller/auth").WithTags("Vendeur · Auth").RequireRateLimiting("auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        auth.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Demande de réinitialisation du mot de passe.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Ces deux routes manquaient à la surface vendeur, alors qu'elles existent mot
    /// pour mot côté Admin (`AdminAuthEndpoints`) et côté acheteur. Un vendeur qui
    /// oubliait son mot de passe n'avait AUCUN recours : il fallait une intervention
    /// manuelle en base. Sur une place de marché où le vendeur ne se connecte parfois
    /// qu'une fois par semaine, c'est le premier motif de support.
    ///
    /// Aucun code de domaine n'est ajouté : `RequestPasswordResetCommand` et
    /// `ResetPasswordCommand` existaient déjà et sont partagés par les trois surfaces.
    /// Ce sont les deux points d'entrée HTTP qui manquaient.
    ///
    /// ⚠️ RÉPONSE VOLONTAIREMENT AVEUGLE — `204` que l'adresse existe ou non. Répondre
    /// « compte inconnu » transformerait cette route en oracle d'existence de comptes
    /// vendeurs, exploitable pour dresser la liste des boutiques de la plateforme.
    /// C'est précisément le défaut relevé par l'audit sur le renvoi de vérification
    /// (§2.6) ; on ne l'introduit pas ici.
    ///
    /// Le jeton généré n'est jamais renvoyé par l'API : il part par e-mail. Le groupe
    /// porte déjà `RequireRateLimiting("auth")`, qui borne le rythme des tentatives.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request, ISender sender, CancellationToken ct)
    {
        // Le résultat est délibérément ignoré : succès comme échec donnent 204.
        _ = await sender.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.NoContent();
    }

    /// <summary>Réinitialise le mot de passe à partir du jeton reçu par e-mail.</summary>
    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequestBody request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct))
            .Match(() => Results.NoContent());

    /// <summary>Connexion : renvoie access + refresh token.</summary>
    /// <summary>
    /// Connexion à l'app vendeur. Le rôle Seller est exigé DÈS LA PORTE : sans lui,
    /// un acheteur obtenait un jeton valide, l'app s'ouvrait, et il se heurtait à un
    /// 403 sur chaque écran sans comprendre pourquoi.
    /// </summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new LoginCommand(request.Email, request.Password, request.MfaCode, SellerRoles), ct))
            .Match(r => Results.Ok(r));

    private static readonly string[] SellerRoles = [SellerBff.SellerRole];

    /// <summary>Rafraîchit l'access token.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct)).Match(r => Results.Ok(r));

    /// <summary>Invalide le refresh token.</summary>
    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct)).Match(() => Results.NoContent());

    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequestBody(string Email, string Token, string NewPassword);
}
