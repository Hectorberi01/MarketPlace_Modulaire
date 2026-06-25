using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — authentification (/mobile/auth/*). Payloads compacts.</summary>
public static class MobileAuthEndpoints
{
    public static IEndpointRouteBuilder MapMobileAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/mobile/auth").WithTags("Mobile · Auth");

        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        // OTP par SMS et réinitialisation de mot de passe : nécessitent une capacité
        // Identity dédiée (envoi SMS / jeton de reset) non encore implémentée.
        auth.MapPost("/otp/request", () => MobileBff.NotImplemented("identity.sms_otp")).AllowAnonymous();
        auth.MapPost("/otp/verify", () => MobileBff.NotImplemented("identity.sms_otp")).AllowAnonymous();
        auth.MapPost("/password/forgot", () => MobileBff.NotImplemented("identity.password_reset")).AllowAnonymous();
        auth.MapPost("/password/reset", () => MobileBff.NotImplemented("identity.password_reset")).AllowAnonymous();

        return app;
    }

    /// <summary>Création de compte (email/téléphone + mot de passe).</summary>
    private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new RegisterUserCommand(request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty, request.Password), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Connexion : renvoie access + refresh token.</summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct)).Match(r => Results.Ok(r));

    /// <summary>Rafraîchit l'access token.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct)).Match(r => Results.Ok(r));

    /// <summary>Invalide le refresh token.</summary>
    private static async Task<IResult> LogoutAsync(System.Security.Claims.ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct)).Match(() => Results.NoContent());
    }

    public sealed record RegisterRequest(string FirstName, string LastName, string Email, string? PhoneNumber, string Password);
    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
}
