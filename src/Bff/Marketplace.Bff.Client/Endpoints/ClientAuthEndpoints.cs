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
        => (await sender.Send(new ConfirmEmailCommand(request.Email, request.Token), ct))
            .Match(() => Results.Ok(new { confirmed = true }));

    /// <summary>
    /// Renvoie un code de vérification à un compte existant non vérifié.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA RÉPONSE EST CONSTANTE. C'ÉTAIT UN ORACLE, ET IL EST FERMÉ.
    ///
    /// Cette route rendait `{ userId: <guid> }` quand l'adresse était connue et
    /// `{ userId: null }` sinon. Sans aucun jeton, on testait une liste d'adresses et on
    /// savait lesquelles avaient un compte — et chaque test positif ENVOYAIT UN VRAI
    /// E-MAIL à la personne visée : harcèlement, et usure de la réputation d'expéditeur
    /// du domaine. La limitation de débit bornait le rythme, pas la faisabilité.
    ///
    /// L'identifiant était rendu parce que l'écran de saisie du code en avait besoin pour
    /// confirmer ensuite. C'est ce lien qui a été coupé : `ConfirmEmailCommand` prend
    /// désormais l'ADRESSE, que l'application connaît déjà puisque l'utilisateur vient de
    /// la saisir. Plus rien n'a besoin d'être divulgué.
    ///
    /// LE RÉSULTAT EST IGNORÉ, comme sur `password/forgot` trente lignes plus haut — les
    /// deux routes se contredisaient à cette distance.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ResendEmailAsync(
        ResendRequest request, ISender sender, IIdentityModuleApi identity, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // LE COMMENTAIRE DISAIT « COMPTE NON VÉRIFIÉ ». LE CODE NE LE CONTRÔLAIT PAS.
        //
        // `BeginEmailVerification` ne regarde ni `EmailVerified` ni le statut : il pose
        // un code, point. N'importe quel compte EXISTANT recevait donc un e-mail —
        // vérifié, actif, suspendu, supprimé. Avec une liste d'adresses clients, on
        // arrosait la base entière. C'est le harcèlement et l'usure de réputation
        // d'expéditeur que la correction de cette route nommait comme sa raison d'être,
        // encore présents par le canal de l'effet de bord après avoir été fermés dans
        // celui de la réponse.
        //
        // LA GARDE EST ICI, PAS DANS LE DOMAINE. `BeginEmailVerification` doit continuer
        // d'accepter un compte déjà vérifié : c'est ce qui permet à un acheteur qui se
        // lance dans la vente de prouver qu'il relève sa boîte avant qu'on lui rattache
        // une boutique. La règle « seulement si non vérifié » est propre à CETTE
        // surface, où confirmer son adresse est la seule chose que le code sert à faire.
        // ═════════════════════════════════════════════════════════════════════════
        var user = await identity.GetUserByEmailAsync(request.Email, ct);
        if (user is not null && !user.EmailVerified && IsResendEligible(user.Status))
        {
            _ = await sender.Send(new RequestEmailVerificationCommand(user.Id), ct);
        }

        return Results.Ok(new
        {
            sent = true,
            message = "Si un compte existe pour cette adresse et n'est pas encore vérifié, "
                    + "un nouveau lien vient d'être envoyé."
        });
    }

    /// <summary>
    /// Un compte dans cet état peut-il recevoir un nouveau code ?
    ///
    /// Liste BLANCHE, et les noms viennent de l'énumération `UserStatus`. Un compte
    /// suspendu ou supprimé n'a rien à confirmer : lui envoyer un code ne lui rend aucun
    /// service et donne à l'expéditeur d'une boucle de sondage un e-mail de plus.
    /// </summary>
    private static bool IsResendEligible(string status)
        => string.Equals(status, "PendingVerification", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Réinitialise le mot de passe à partir du jeton reçu.</summary>
    private static async Task<IResult> ResetPasswordAsync(ResetRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), ct))
            .Match(() => Results.Ok(new { reset = true }));

    /// <summary>
    /// Création de compte.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// L'ORACLE FERMÉ CÔTÉ VENDEUR ÉTAIT RESTÉ OUVERT ICI, TROIS FICHIERS PLUS LOIN.
    ///
    /// La route rendait `409 identity.user.email_taken` sur une adresse prise et
    /// `200 { id }` sur une adresse libre : EXISTENCE ET IDENTIFIANT INTERNE, en une
    /// seule requête anonyme. C'est la paire exacte que `/seller/auth/register` a passé
    /// cent lignes de commentaire à supprimer — sur une route d'inscription voisine, qui
    /// appelle la même commande.
    ///
    /// La branche `identity.user.phone_taken` énumérait en plus les NUMÉROS, un
    /// identifiant qu'aucune surface n'avait encore été durcie contre.
    ///
    /// Réponse constante désormais, et plus de Guid. Les erreurs de SAISIE, elles,
    /// remontent telles quelles : elles portent sur ce que l'appelant vient de taper,
    /// pas sur ce que la base contient, et les taire empêcherait de corriger un
    /// formulaire. Le pipeline de validation s'exécute avant le handler, donc identique
    /// dans les deux cas.
    ///
    /// AUCUN CODE N'EST RENVOYÉ QUAND L'ADRESSE EST DÉJÀ PRISE — contrairement au
    /// parcours vendeur, qui en émet un. La différence est justifiée : là-bas, un
    /// acheteur qui se lance doit PROUVER qu'il relève la boîte avant qu'on lui rattache
    /// une boutique. Ici il n'y a rien à rattacher ; la bonne action est de se
    /// connecter, et envoyer un e-mail à chaque sondage ne servirait qu'à harceler.
    ///
    /// CE QUI RESTE, ET QUI EST INHÉRENT : sonder une adresse LIBRE crée un vrai compte.
    /// On peut donc squatter une adresse non inscrite — sa titulaire ne pourra plus
    /// s'inscrire et recevra un code qu'elle n'a pas demandé. Fermer ça demanderait une
    /// épreuve avant création (captcha, ou création différée jusqu'à la validation du
    /// code). C'est une décision produit, pas une correction.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
    {
        var created = await sender.Send(new RegisterUserCommand(
            request.FirstName, request.LastName, request.Email,
            request.PhoneNumber ?? string.Empty, request.Password), ct);

        // Les deux conflits d'existence sont AVALÉS, y compris `phone_taken`. Le taire
        // laisse un inscrit légitime dont le numéro est déjà pris attendre un code qui
        // ne viendra pas — d'où la dernière phrase du message. Le dire rouvrirait
        // l'énumération sur le numéro au lieu de l'adresse.
        if (created.IsFailure && !IsExistingAccountConflict(created.Error.Code))
        {
            return created.Match(_ => Results.Ok());
        }

        return Results.Ok(new
        {
            registered = true,
            requiresVerification = true,
            message = "Si cette adresse n'a pas déjà de compte, un code à 6 chiffres vient "
                    + "d'être envoyé. Si vous en avez déjà un, connectez-vous. Sans code d'ici "
                    + "quelques minutes, vérifiez les informations saisies ou contactez l'assistance."
        });
    }

    /// <summary>
    /// Ce code d'erreur signifie-t-il « ce compte existe déjà » ?
    ///
    /// Les deux valeurs viennent de `RegisterUserCommandHandler`, et sont listées
    /// explicitement plutôt que déduites d'un préfixe : le jour où une troisième erreur
    /// de conflit apparaîtra, il vaut mieux qu'elle remonte à l'appelant — visible, donc
    /// corrigeable — plutôt qu'elle disparaisse dans la réponse uniforme.
    /// </summary>
    private static bool IsExistingAccountConflict(string code)
        => code is "identity.user.email_taken" or "identity.user.phone_taken";

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
    public sealed record ConfirmEmailRequest(string Email, string Token);
    public sealed record ResendRequest(string Email);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Email, string Token, string NewPassword);
}
