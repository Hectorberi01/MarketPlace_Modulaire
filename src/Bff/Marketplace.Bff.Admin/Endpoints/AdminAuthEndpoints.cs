using MediatR;
using Microsoft.AspNetCore.Http;
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
    /// <summary>
    /// Connexion à la console d'administration.
    ///
    /// Les groupes /admin exigeaient déjà le rôle : un acheteur n'aurait rien pu
    /// FAIRE. Mais le login, lui, réussissait — la console lui confirmait donc que
    /// son mot de passe était bon sur la surface d'administration. C'est un signal
    /// qu'on ne donne pas.
    /// </summary>
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        AdminAuthCookieOptions cookieOptions,
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(
            new LoginCommand(request.Email, request.Password, request.MfaCode, AdminRoles), ct);

        if (result.IsFailure)
        {
            return result.Match(r => Results.Ok(r));
        }

        var response = result.Value;

        // MFA demandée : pas encore de jetons, donc pas de cookie.
        if (response.MfaRequired || response.Tokens is null)
        {
            return Results.Ok(new { mfaRequired = true });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LE JETON DE RAFRAÎCHISSEMENT NE SORT PLUS DANS LE CORPS JSON.
        //
        // Avant, il était renvoyé ici, et la console Blazor le rangeait dans
        // `localStorage` — lisible par n'importe quel script. Une XSS l'exfiltrait, et
        // l'attaquant gardait 30 jours d'accès administrateur depuis sa propre machine.
        //
        // Désormais il part en cookie HttpOnly : le navigateur le renverra tout seul sur
        // /admin/auth, et AUCUN script ne peut le lire. La console ne le voit jamais —
        // elle ne peut donc pas le divulguer, même si elle est compromise.
        // ─────────────────────────────────────────────────────────────────────────
        RefreshTokenCookie.Issue(httpContext, response.Tokens, cookieOptions.SameSite);

        return Results.Ok(AdminAuthResponse.From(response.Tokens));
    }

    private static readonly string[] AdminRoles = [AdminBff.AdminRole, AdminBff.ModeratorRole];

    /// <summary>
    /// Rafraîchit le jeton d'accès à partir du COOKIE (plus du corps de la requête).
    ///
    /// Le corps est désormais vide : le jeton de rafraîchissement n'est plus manipulé par
    /// la console, donc elle ne peut pas l'envoyer — ni le divulguer.
    /// </summary>
    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        AdminAuthCookieOptions cookieOptions,
        ISender sender,
        CancellationToken ct)
    {
        // GARDE ANTI-CSRF, ET ELLE N'EST PAS FACULTATIVE.
        //
        // Un cookie part TOUT SEUL avec la requête. Sans cette vérification, un site
        // malveillant pourrait faire soumettre au navigateur de l'administrateur un
        // formulaire vers cette route : le cookie suivrait, et l'attaquant récupérerait un
        // jeton d'accès. On aurait fermé le vol de jeton par XSS en ouvrant le CSRF.
        //
        // Un formulaire HTML ne peut PAS poser d'en-tête personnalisé. Seul un fetch/XHR le
        // peut — et un fetch cross-origin portant un en-tête personnalisé déclenche un
        // preflight CORS, que notre politique refuse pour toute origine non déclarée.
        if (!httpContext.Request.Headers.ContainsKey(RefreshTokenCookie.AntiCsrfHeader))
        {
            return Results.Problem(
                title: "admin.auth.csrf_header_missing",
                detail: $"L'en-tête {RefreshTokenCookie.AntiCsrfHeader} est obligatoire sur cette route.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var refreshToken = RefreshTokenCookie.Read(httpContext);
        if (refreshToken is null)
        {
            // Pas de cookie : session absente ou expirée. 401 — la console redirige vers
            // l'écran de connexion.
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RefreshTokenCommand(refreshToken), ct);
        if (result.IsFailure)
        {
            // Jeton révoqué, expiré, ou inconnu. On EFFACE le cookie : le garder ferait
            // rejouer indéfiniment un jeton mort à chaque chargement de page.
            RefreshTokenCookie.Clear(httpContext, cookieOptions.SameSite);
            return result.Match(r => Results.Ok(r));
        }

        // ROTATION : la commande émet un nouveau jeton de rafraîchissement et révoque
        // l'ancien. Le cookie doit suivre, sinon la prochaine tentative présenterait un
        // jeton déjà révoqué et l'administrateur serait éjecté sans raison.
        RefreshTokenCookie.Issue(httpContext, result.Value, cookieOptions.SameSite);

        return Results.Ok(AdminAuthResponse.From(result.Value));
    }

    /// <summary>
    /// Déconnexion : révoque le jeton de rafraîchissement côté serveur ET efface le cookie.
    ///
    /// Les deux, et dans cet ordre. Effacer le cookie sans révoquer laisserait un jeton
    /// valide dans la nature ; révoquer sans effacer laisserait le navigateur rejouer un
    /// jeton mort à chaque chargement.
    /// </summary>
    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user,
        HttpContext httpContext,
        AdminAuthCookieOptions cookieOptions,
        ISender sender,
        CancellationToken ct)
    {
        if (AdminBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var refreshToken = RefreshTokenCookie.Read(httpContext);

        // Le cookie est effacé DANS TOUS LES CAS — même si la révocation échoue, même s'il
        // n'y avait pas de cookie. Une déconnexion qui ne déconnecte pas est pire que pas
        // de déconnexion : elle rassure à tort.
        RefreshTokenCookie.Clear(httpContext, cookieOptions.SameSite);

        if (refreshToken is null)
        {
            return Results.NoContent();
        }

        // Le résultat est ignoré à dessein : que la révocation réussisse ou échoue (jeton
        // déjà expiré, par exemple), le cookie est effacé et l'administrateur est
        // déconnecté. Renvoyer une erreur ici le laisserait croire qu'il est encore
        // connecté alors que sa session locale est vidée.
        _ = await sender.Send(new LogoutCommand(userId, refreshToken), ct);
        return Results.NoContent();
    }

    public sealed record LoginRequest(string Email, string Password, string? MfaCode);

    // RefreshRequest / LogoutRequest ont DISPARU, et c'est le cœur du correctif : le jeton
    // de rafraîchissement ne transite plus par le corps des requêtes. Il n'existe, côté
    // navigateur, que sous forme de cookie HttpOnly — donc hors de portée de tout script.
    // Ne les réintroduisez pas : ce serait rouvrir la porte du localStorage.
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequestBody(string Email, string Token, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
}
