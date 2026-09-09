using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// Authentification du coursier — /driver/auth/*.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE ROUTE D'INSCRIPTION, ET CE N'EST PAS UN OUBLI.
///
/// On ne devient pas coursier en remplissant un formulaire. Un coursier est enregistré
/// par la console d'administration (POST /admin/couriers), avec ses coordonnées de
/// versement, puis ACTIVÉ après vérification. C'est cette activation qui lui donne le
/// droit de travailler — et qui l'annonce au service de dispatch.
///
/// Exposer un /driver/auth/register créerait des comptes que rien ne rattache à un
/// coursier : ils passeraient l'authentification et se heurteraient à « not_a_courier »
/// sur chaque route. Une porte qui ne mène nulle part est pire que pas de porte.
///
/// Le jeton émis ici est le MÊME que partout ailleurs — clé, émetteur et audience
/// partagés par les cinq hôtes. Il vaut donc aussi pour le service de dispatch, qui le
/// vérifie sans en émettre. C'est ce qui permet à l'application coursier de parler aux
/// deux avec une seule connexion.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DriverAuthEndpoints
{
    public static IEndpointRouteBuilder MapDriverAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/driver/auth").WithTags("Driveur · Auth").RequireRateLimiting("auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        auth.MapPost("/password/forgot", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/password/reset", ResetPasswordAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct))
            .Match(r => Results.Ok(r));

    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct)).Match(r => Results.Ok(r));

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
    {
        if (DriverBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>
    /// Demande de réinitialisation.
    ///
    /// LE RÉSULTAT EST VOLONTAIREMENT IGNORÉ : succès comme échec produisent la MÊME
    /// réponse. Renvoyer une erreur sur une adresse inconnue rétablirait exactement
    /// l'énumération de comptes que cette route cherche à empêcher. Même règle que sur
    /// les autres surfaces — un écart ici suffirait à rouvrir le trou partout, puisqu'il
    /// suffit d'interroger l'hôte le plus bavard.
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        ForgotRequest request, ISender sender, CancellationToken ct)
    {
        _ = await sender.Send(new RequestPasswordResetCommand(request.Email), ct);

        return Results.Ok(new
        {
            sent = true,
            message = "Si un compte existe pour cette adresse, un e-mail de réinitialisation vient d'être envoyé."
        });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct))
            .Match(() => Results.NoContent());

    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Email, string Token, string NewPassword);
}
