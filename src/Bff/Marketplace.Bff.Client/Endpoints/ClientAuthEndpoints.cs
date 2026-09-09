using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;
using Marketplace.Modules.Identity.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>BFF Client — authentification (/client/auth/*). Payloads compacts.</summary>
public static class ClientAuthEndpoints
{
    public static IEndpointRouteBuilder MapClientAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/client/auth").WithTags("Client · Auth").RequireRateLimiting("auth");

        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();

        // Réinitialisation de mot de passe (jeton à usage unique, 1h).
        auth.MapPost("/password/forgot", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/password/reset", ResetPasswordAsync).AllowAnonymous();

        // Confirmation d'adresse e-mail.
        //
        // CETTE ROUTE N'EXISTAIT PAS. `ConfirmEmailCommand` était écrite, testée, et
        // AUCUN endpoint ne l'appelait : même si l'e-mail de vérification était parti
        // (il ne partait pas non plus), le lien n'aurait mené nulle part. La chaîne était
        // rompue à chacun de ses maillons.
        //
        // C'est un POST, jamais un GET : les clients de messagerie et les passerelles
        // antivirus PRÉ-CHARGENT les liens des e-mails pour les analyser. Une confirmation
        // sur GET serait déclenchée par le scanner, avant même que l'utilisateur ne clique.
        auth.MapPost("/email/confirm", ConfirmEmailAsync).AllowAnonymous();

        // Renvoi du code de vérification : un acheteur qui a abandonné la
        // vérification puis revient sur l'écran de connexion peut s'en refaire
        // envoyer un.
        auth.MapPost("/email/resend", ResendEmailAsync).AllowAnonymous();

        // OTP par SMS : nécessite un fournisseur SMS (non encore branché).
        auth.MapPost("/otp/request", () => ClientBff.NotImplemented("identity.sms_otp")).AllowAnonymous();
        auth.MapPost("/otp/verify", () => ClientBff.NotImplemented("identity.sms_otp")).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Demande de réinitialisation. Renvoie TOUJOURS la même réponse — succès, échec, ou
    /// e-mail inconnu — afin qu'on ne puisse pas énumérer les comptes existants.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CET ENDPOINT DIVULGUAIT LE JETON DE RÉINITIALISATION. À TOUT LE MONDE.
    ///
    /// Il renvoyait littéralement ceci, en ANONYME, pour n'importe quelle adresse saisie :
    ///
    ///     // TODO production : envoyer result.Value par e-mail/SMS et ne pas l'exposer ici.
    ///     return Results.Ok(new { sent = true, token = result.Value });
    ///
    /// Deux requêtes suffisaient pour prendre n'importe quel compte : `password/forgot`
    /// avec l'e-mail de la victime pour récolter le jeton, puis `password/reset` avec ce
    /// jeton. Les cinq hôtes partagent la même base Identity : un compte administrateur
    /// tombait aussi facilement qu'un compte acheteur. Prise de contrôle totale de la
    /// plateforme, sans authentification.
    ///
    /// La cause n'était pas la négligence, mais l'absence : il n'existait AUCUN canal
    /// e-mail dans le dépôt. Le jeton n'avait nulle part où aller, alors il est sorti par
    /// la seule porte ouverte — la réponse HTTP.
    ///
    /// Désormais, `RequestPasswordResetCommand` ne renvoie plus RIEN (`ICommand` nu) : cet
    /// endpoint ne peut plus divulguer un jeton qu'il ne reçoit pas. Le jeton part par
    /// l'outbox vers Notifications, qui l'envoie par e-mail à son propriétaire.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(ForgotRequest request, ISender sender, CancellationToken ct)
    {
        // Le résultat est volontairement ignoré : succès comme échec produisent la MÊME
        // réponse. Renvoyer une erreur sur un e-mail inconnu rétablirait l'énumération de
        // comptes qu'on cherche précisément à empêcher.
        _ = await sender.Send(new RequestPasswordResetCommand(request.Email), ct);

        return Results.Ok(new
        {
            sent = true,
            message = "Si un compte existe pour cette adresse, un e-mail de réinitialisation vient d'être envoyé.",
        });
    }

    /// <summary>Confirme l'adresse e-mail à partir du lien reçu par mail.</summary>
    private static async Task<IResult> ConfirmEmailAsync(ConfirmEmailRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmEmailCommand(request.UserId, request.Token), ct))
            .Match(() => Results.Ok(new { confirmed = true }));

    /// <summary>
    /// Renvoie un code de vérification à un compte EXISTANT non vérifié.
    ///
    /// CETTE RÉPONSE N'EST PAS NEUTRE, contrairement à ce qu'affirmait ce
    /// commentaire. Elle rend `{ userId: <guid> }` si l'adresse est connue et
    /// `{ userId: null }` sinon : c'est un ORACLE D'EXISTENCE de compte, exploitable
    /// sans authentification, et chaque appel positif envoie un e-mail à la victime.
    ///
    /// Ce n'est pas corrigé ici, et c'est un choix : l'écran de saisie du code a
    /// besoin de cet `userId` pour appeler `confirm-email`. Neutraliser la réponse
    /// sans livrer en même temps la version correspondante de l'application casserait
    /// la vérification d'e-mail pour tous les nouveaux comptes.
    ///
    /// CORRECTION À MENER CONJOINTEMENT (backend + app) :
    ///   1. `ConfirmEmailCommand` accepte l'e-mail à la place de l'`userId` ;
    ///   2. cette route renvoie toujours `{ sent: true }`, quoi qu'il arrive ;
    ///   3. l'app garde l'e-mail saisi plutôt que l'identifiant.
    ///
    /// En attendant, le seul rempart est la limitation de débit (30/min par IP) — qui
    /// borne le rythme, pas la faisabilité. Même remarque pour
    /// `SellerRegistrationEndpoints.ResendAsync`.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ResendEmailAsync(
        ResendRequest request, ISender sender, IIdentityModuleApi identity, CancellationToken ct)
    {
        var user = await identity.GetUserByEmailAsync(request.Email, ct);
        if (user is null)
        {
            return Results.Ok(new { userId = (Guid?)null });
        }

        _ = await sender.Send(new RequestEmailVerificationCommand(user.Id), ct);
        return Results.Ok(new { userId = (Guid?)user.Id });
    }

    /// <summary>Réinitialise le mot de passe à partir du jeton reçu.</summary>
    private static async Task<IResult> ResetPasswordAsync(ResetRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct))
            .Match(() => Results.Ok(new { reset = true }));

    /// <summary>Création de compte (email/téléphone + mot de passe).</summary>
    private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new RegisterUserCommand(request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty, request.Password), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Connexion : renvoie access + refresh token.</summary>
    /// <summary>
    /// Connexion à l'app acheteur.
    ///
    /// AUCUN rôle exigé, et c'est délibéré : c'est la seule des trois surfaces qui
    /// doit rester ouverte à tous. Un vendeur est aussi quelqu'un qui achète ; lui
    /// imposer un second compte pour commander une paire de chaussures serait une
    /// régression, pas une sécurité.
    /// </summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct)).Match(r => Results.Ok(r));

    /// <summary>Rafraîchit l'access token.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct)).Match(r => Results.Ok(r));

    /// <summary>Invalide le refresh token.</summary>
    private static async Task<IResult> LogoutAsync(System.Security.Claims.ClaimsPrincipal user, LogoutRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct)).Match(() => Results.NoContent());
    }

    public sealed record RegisterRequest(string FirstName, string LastName, string Email, string? PhoneNumber, string Password);
    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record ConfirmEmailRequest(Guid UserId, string Token);
    public sealed record ResendRequest(string Email);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Email, string Token, string NewPassword);
}
