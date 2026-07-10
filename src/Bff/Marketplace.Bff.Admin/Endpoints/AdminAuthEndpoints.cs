using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Application.Users.Commands.Mfa;

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
        // Rate limiting « auth » (fenêtre fixe par IP) sur tout le groupe : freine
        // le brute-force / l'énumération sur login, refresh et reset de mot de passe.
        var auth = app.MapGroup("/admin/auth").WithTags("Admin · Auth").RequireRateLimiting("auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        auth.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous();
        auth.MapPost("/mfa/setup", BeginMfaAsync).RequireAuthorization();
        auth.MapPost("/mfa/confirm", ConfirmMfaAsync).RequireAuthorization();
        auth.MapPost("/mfa/disable", DisableMfaAsync).RequireAuthorization();

        return app;
    }

    /// <summary>Démarre l'activation MFA : renvoie le secret + l'URI otpauth à scanner.</summary>
    private static async Task<IResult> BeginMfaAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new BeginMfaSetupCommand(userId), ct)).Match(r => Results.Ok(r));

    /// <summary>Confirme l'activation MFA avec un code TOTP.</summary>
    private static async Task<IResult> ConfirmMfaAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ConfirmMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    /// <summary>Désactive le MFA (nécessite un code TOTP valide).</summary>
    private static async Task<IResult> DisableMfaAsync(ClaimsPrincipal user, MfaCodeRequest request, ISender sender, CancellationToken ct)
        => AdminBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DisableMfaCommand(userId, request.Code), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Demande de réinitialisation : génère un jeton (valide 1 h) qui doit être
    /// acheminé HORS-BANDE (e-mail). Le jeton n'est JAMAIS renvoyé dans la réponse
    /// HTTP — sinon n'importe quel appelant anonyme obtiendrait un jeton valide pour
    /// l'e-mail d'une victime (prise de contrôle de compte). Réponse constante
    /// (204) pour ne pas divulguer l'existence d'un compte (pas d'énumération).
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request, ISender sender, CancellationToken ct)
    {
        // On déclenche la génération du jeton (pour l'envoi e-mail à venir) mais on
        // ignore délibérément sa valeur : elle ne doit pas transiter par l'API.
        _ = await sender.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.NoContent();
    }

    /// <summary>Réinitialise le mot de passe avec le jeton reçu.</summary>
    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequestBody request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct)).Match(() => Results.NoContent());

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
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequestBody(string Email, string Token, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
}
