using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ApproveUser;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;
using Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — AUTO-INSCRIPTION depuis l'app (/seller/auth/register + /verify).
///
/// Le vendeur crée lui-même son compte. Deux cas, un seul parcours côté app :
///
///  • E-mail inconnu → on crée le compte (rôle Buyer, PendingVerification) et on
///    envoie un CODE à 6 chiffres.
///  • E-mail déjà connu (un acheteur qui se lance) → on n'en recrée pas ; on émet
///    un code pour PROUVER qu'il relève bien cette boîte avant de lui rattacher une
///    boutique. Sans cette preuve, n'importe qui rattacherait une boutique au compte
///    d'autrui.
///
/// La boutique n'est créée qu'à l'étape /verify, une fois le code validé — car
/// l'onboarding vendeur EXIGE un e-mail vérifié. À la validation, le compte est
/// activé (il doit pouvoir se connecter et compléter son KYB) ; la PUBLICATION DE
/// PRODUITS, elle, reste gatée par la validation admin (KYB + activation vendeur).
///
/// Non transactionnel : si une étape tardive échoue après la création de la
/// boutique, l'état partiel subsiste (même caveat que l'onboarding admin) — à
/// durcir en saga si nécessaire.
/// </summary>
public static class SellerRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapSellerRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/seller/auth").WithTags("Vendeur · Auth").RequireRateLimiting("auth");

        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapPost("/verify", VerifyAsync).AllowAnonymous();
        // Renvoi du code : un vendeur qui a abandonné la vérification puis revient
        // sur l'écran de connexion peut se refaire envoyer un code.
        auth.MapPost("/verify/resend", ResendCodeAsync).AllowAnonymous();

        // Mot de passe oublié : demande d'un code par e-mail, puis réinitialisation.
        auth.MapPost("/password/forgot", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/password/reset", ResetPasswordAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Envoie un code de réinitialisation à l'e-mail (réponse identique si compte inconnu).</summary>
    private static async Task<IResult> ForgotPasswordAsync(SellerForgotRequest request, ISender sender, CancellationToken ct)
    {
        // Résultat volontairement ignoré : succès comme échec renvoient la MÊME réponse,
        // pour ne pas révéler si un compte existe (anti-énumération).
        _ = await sender.Send(new RequestPasswordResetCommand(request.Email), ct);
        return Results.Ok(new
        {
            sent = true,
            message = "Si un compte existe pour cette adresse, un code de réinitialisation vient d'être envoyé.",
        });
    }

    /// <summary>Réinitialise le mot de passe à partir du code reçu par e-mail.</summary>
    private static async Task<IResult> ResetPasswordAsync(SellerResetRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResetPasswordCommand(request.Email, request.Code, request.NewPassword), ct))
            .Match(() => Results.Ok(new { reset = true }));

    /// <summary>
    /// Renvoie un code de vérification à un compte EXISTANT non vérifié. Utilisé
    /// depuis l'écran de connexion quand l'utilisateur a abandonné la vérification.
    /// Renvoie l'UserId (nécessaire à l'écran de saisie du code) quand le compte
    /// existe ; sinon un corps neutre — l'app affiche le même message dans tous les cas.
    /// </summary>
    private static async Task<IResult> ResendCodeAsync(
        SellerResendRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        CancellationToken ct)
    {
        var user = await identity.GetUserByEmailAsync(request.Email, ct);
        if (user is null)
        {
            return Results.Ok(new SellerResendResponse(UserId: null));
        }

        _ = await sender.Send(new RequestEmailVerificationCommand(user.Id), ct);
        return Results.Ok(new SellerResendResponse(UserId: user.Id));
    }


    /// <summary>
    /// Ce compte peut-il ouvrir une boutique ?
    ///
    /// ⚠️ LISTE BLANCHE, et les noms viennent de l'énumération `UserStatus` :
    /// `PendingVerification`, `Active`, `Suspended`, `Deleted`. Écrire « Pending » —
    /// le raccourci naturel — n'aurait correspondu à AUCUNE valeur, et aurait donc
    /// bloqué tous les comptes fraîchement inscrits, qui sont précisément ceux que ce
    /// parcours doit servir. Une liste blanche mal orthographiée refuse tout ; une
    /// liste noire mal orthographiée laisse tout passer. La première se voit au
    /// premier essai, la seconde jamais.
    ///
    /// `Suspended` et `Deleted` sont exclus : c'est tout l'objet du contrôle.
    /// </summary>
    private static bool IsEligibleForOnboarding(string status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "PendingVerification", StringComparison.OrdinalIgnoreCase);

    /// <summary>Étape 1 : crée le compte (ou identifie l'existant) et envoie le code.</summary>
    private static async Task<IResult> RegisterAsync(
        SellerSelfRegisterRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        var existing = await identity.GetUserByEmailAsync(request.Email, ct);

        if (existing is null)
        {
            return (await sender.Send(new RegisterUserCommand(
                    request.FirstName, request.LastName, request.Email,
                    request.PhoneNumber ?? string.Empty, request.Password, CreatedByAdmin: false), ct))
                .Match(userId => Results.Ok(new SellerRegisterResponse(userId, IsNewAccount: true)));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UN COMPTE SUSPENDU OU FERMÉ NE PASSE PAS PAR ICI.
        //
        // Le seul contrôle était « ce compte a-t-il déjà une boutique ? ». Le STATUT
        // n'était jamais regardé — et la suite du parcours, elle, appelle
        // `ApproveUserCommand`, qui fait passer le compte à `Active` DEPUIS N'IMPORTE
        // QUEL état, suspension comprise.
        //
        // Autrement dit : un vendeur suspendu pour fraude, qui garde l'accès à sa boîte
        // mail, se réactivait tout seul en deux requêtes anonymes — nouveau rôle
        // Seller, nouvelle boutique, aucun KYB, aucune intervention humaine. La
        // sanction administrative ne valait rien.
        //
        // On répond ici, AVANT d'envoyer le moindre code : rien ne doit inviter à
        // poursuivre un parcours qui se refermera de toute façon.
        //
        // Le message reste vague à dessein — « contactez l'assistance » — pour ne pas
        // renseigner sur le motif ni confirmer le statut à un appelant anonyme.
        // ─────────────────────────────────────────────────────────────────────────
        if (!IsEligibleForOnboarding(existing.Status))
        {
            return Results.Problem(
                title: "identity.user.not_eligible",
                detail: "Ce compte ne peut pas ouvrir de boutique. Contactez l'assistance.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var shop = await sellers.GetSellerByUserIdAsync(existing.Id, ct);
        if (shop is not null)
        {
            return Results.Problem(
                title: "sellers.seller.already_seller",
                detail: "Ce compte est déjà rattaché à une boutique.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return (await sender.Send(new RequestEmailVerificationCommand(existing.Id), ct))
            .Match(() => Results.Ok(new SellerRegisterResponse(existing.Id, IsNewAccount: false)));
    }

    /// <summary>Étape 2 : valide le code, crée la boutique, attribue le rôle, active le compte.</summary>
    private static async Task<IResult> VerifyAsync(
        SellerVerifyRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        CancellationToken ct)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // LE CONTRÔLE DE STATUT EST REFAIT ICI, ET CE N'EST PAS REDONDANT.
        //
        // `/register` et `/verify` sont deux endpoints ANONYMES distincts. Rien
        // n'oblige un appelant à passer par le premier : il suffit d'un identifiant de
        // compte et d'un code valide — obtenus lors d'une inscription antérieure,
        // avant la suspension — pour attaquer `/verify` directement.
        //
        // Or c'est `/verify` qui attribue le rôle Seller et appelle `ApproveUserCommand`.
        // Poser la barrière uniquement à l'entrée revenait à fermer la porte en
        // laissant la fenêtre ouverte.
        // ─────────────────────────────────────────────────────────────────────────
        var account = await identity.GetUserAsync(request.UserId, ct);
        if (account is null || !IsEligibleForOnboarding(account.Status))
        {
            return Results.Problem(
                title: "identity.user.not_eligible",
                detail: "Ce compte ne peut pas ouvrir de boutique. Contactez l'assistance.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // 1. Code (strict : preuve de possession de la boîte).
        var verified = await sender.Send(new VerifyEmailCodeCommand(request.UserId, request.Code), ct);
        if (verified.IsFailure)
        {
            return verified.Match(() => Results.Ok());
        }

        // 2. Boutique AVANT rôle/activation : c'est l'étape qui peut légitimement
        //    échouer (nom de boutique déjà pris). L'e-mail est désormais vérifié,
        //    donc RegisterSeller l'accepte.
        var metadata = request.Company?.ToDomain();
        var sellerResult = await sender.Send(
            new RegisterSellerCommand(request.UserId, request.ShopName, 0.10m, metadata), ct);
        if (sellerResult.IsFailure)
        {
            return sellerResult.Match(_ => Results.Ok());
        }

        var sellerId = sellerResult.Value;

        // 3. Rôle Seller — sans lui, impossible de se connecter à /seller/*.
        var rolesResult = await sender.Send(new ListRolesQuery(), ct);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Match(_ => Results.Ok());
        }

        var sellerRole = rolesResult.Value.FirstOrDefault(
            r => r.Name.Equals("Seller", StringComparison.OrdinalIgnoreCase));
        if (sellerRole is null)
        {
            // Message NEUTRE : l'ancien (« La base n'a pas été correctement
            // initialisée ») renseignait un appelant anonyme sur l'état interne de
            // l'installation. Le détail appartient aux journaux, pas à la réponse.
            return Results.Problem(
                title: "identity.role.seller_missing",
                detail: "L'inscription vendeur est momentanément indisponible. Réessayez plus tard.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var roleResult = await sender.Send(new AssignRoleCommand(request.UserId, sellerRole.Id), ct);
        if (roleResult.IsFailure)
        {
            return roleResult.Match(() => Results.Ok());
        }

        // 4. Activation du compte : l'e-mail est prouvé, le vendeur doit pouvoir
        //    entrer et compléter son KYB. La publication produit reste gatée ailleurs.
        var approveResult = await sender.Send(new ApproveUserCommand(request.UserId), ct);
        if (approveResult.IsFailure)
        {
            return approveResult.Match(() => Results.Ok());
        }

        return Results.Ok(new SellerVerifyResponse(sellerId, request.UserId));
    }

    // ── Contrats ────────────────────────────────────────────────────────────────

    /// <summary>Corps de l'étape 1. Le mot de passe est ignoré si l'e-mail existe déjà.</summary>
    public sealed record SellerSelfRegisterRequest(
        string Email,
        string Password,
        string FirstName,
        string LastName,
        string? PhoneNumber);

    /// <summary>
    /// Corps de l'étape 2. Le nom de boutique et les infos société sont fournis ici,
    /// une fois le code en main : la boutique n'existe qu'après validation.
    /// </summary>
    public sealed record SellerVerifyRequest(
        Guid UserId,
        string Code,
        string ShopName,
        SellerCompanyInfoDto? Company);

    /// <summary>Infos société déclarées (toutes optionnelles) → metadata jsonb.</summary>
    public sealed record SellerCompanyInfoDto(
        string? LegalName,
        string? Rccm,
        string? Ifu,
        string? Address,
        /// <summary>
        /// Commune de la boutique. Accepte le CODE (« abomey-calavi ») ou le libellé
        /// (« Abomey-Calavi ») : le domaine résout les deux et normalise en code. C'est du
        /// déclaratif de dossier, donc facultatif — contrairement à l'adresse de livraison
        /// et au lieu d'expédition, où la commune est obligatoire.
        /// </summary>
        string? Commune,
        string? Activity,
        string? ManagerName,
        string? Phone)
    {
        public SellerCompanyInfo ToDomain()
            => new(LegalName, Rccm, Ifu, Address,
                   Marketplace.BuildingBlocks.Domain.Geography.BeninGeography.ResolveCommuneCode(Commune),
                   Activity, ManagerName, Phone);
    }

    public sealed record SellerRegisterResponse(Guid UserId, bool IsNewAccount)
    {
        /// <summary>Toujours vrai : l'app doit ensuite demander le code puis /verify.</summary>
        public bool RequiresVerification => true;
    }

    public sealed record SellerVerifyResponse(Guid SellerId, Guid UserId);

    /// <summary>Demande de réinitialisation : juste l'e-mail.</summary>
    public sealed record SellerResendRequest(string Email);

    public sealed record SellerResendResponse(Guid? UserId);

    public sealed record SellerForgotRequest(string Email);

    /// <summary>Réinitialisation : e-mail + code reçu + nouveau mot de passe.</summary>
    public sealed record SellerResetRequest(string Email, string Code, string NewPassword);
}
