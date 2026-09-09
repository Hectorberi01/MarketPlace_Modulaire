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
/// Le vendeur crée lui-même son compte. Deux cas côté serveur, UN SEUL côté réseau :
///
///  • E-mail inconnu → on crée le compte (rôle Buyer, PendingVerification) et on
///    envoie un CODE à 6 chiffres.
///  • E-mail déjà connu (un acheteur qui se lance) → on n'en recrée pas ; on émet
///    un code pour PROUVER qu'il relève bien cette boîte avant de lui rattacher une
///    boutique. Sans cette preuve, n'importe qui rattacherait une boutique au compte
///    d'autrui.
///
/// `/register` RÉPOND LA MÊME CHOSE DANS LES DEUX CAS, et dans les deux refus (compte
/// suspendu, compte déjà vendeur) : la distinction se lisait sans authentification, et
/// renseignait sur qui est vendeur et qui est suspendu. Les refus vivent désormais dans
/// `/verify`, derrière le code — voir RegisterAsync.
///
/// Les deux étapes s'identifient par l'ADRESSE, jamais par un Guid : c'est ce qui permet
/// à `/register` de ne rien rendre.
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
    /// Renvoie un code de vérification à un compte existant non vérifié.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA RÉPONSE EST CONSTANTE. LE COMMENTAIRE PRÉCÉDENT AFFIRMAIT QU'ELLE L'ÉTAIT.
    ///
    /// Il disait « sinon un corps neutre — l'app affiche le même message dans tous les
    /// cas ». Le corps n'était pas neutre : il portait `UserId: null` ou
    /// `UserId: <guid>`. Que l'application affiche le même message ne change rien — c'est
    /// la RÉPONSE HTTP qui est lue par celui qui énumère, pas l'écran.
    ///
    /// On y apprenait donc qu'une adresse a un compte, et on en obtenait le GUID interne.
    /// Sans authentification, et en déclenchant un e-mail réel à chaque coup au but.
    ///
    /// Même correction que sur la surface client : `ConfirmEmailCommand` prend désormais
    /// l'ADRESSE, que l'application connaît déjà. Plus rien n'a besoin d'être rendu.
    /// `ForgotPasswordAsync`, dans ce même fichier, ignorait déjà son résultat pour cette
    /// raison exacte.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ResendCodeAsync(
        SellerResendRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        CancellationToken ct)
    {
        // GARDE DE STATUT, MAIS PAS DE GARDE `EmailVerified` — et la différence avec la
        // surface client est délibérée. Ici le code ne sert pas à confirmer une adresse :
        // il sert à PROUVER qu'on relève la boîte avant qu'une boutique soit rattachée au
        // compte. Un acheteur déjà vérifié qui se lance dans la vente en a donc besoin,
        // et refuser de lui en renvoyer un le bloquerait à l'écran de saisie.
        //
        // Ce qui borne l'arrosage sur cette surface, c'est le délai minimal entre deux
        // envois posé dans `RequestEmailVerificationCommandHandler`.
        var user = await identity.GetUserByEmailAsync(request.Email, ct);
        if (user is not null && IsEligibleForOnboarding(user.Status))
        {
            _ = await sender.Send(new RequestEmailVerificationCommand(user.Id), ct);
        }

        return Results.Ok(new SellerResendResponse(
            Sent: true,
            Message: "Si un compte existe pour cette adresse et n'est pas encore vérifié, "
                   + "un nouveau code vient d'être envoyé."));
    }


    /// <summary>
    /// Ce compte peut-il ouvrir une boutique ?
    ///
    /// LISTE BLANCHE, et les noms viennent de l'énumération `UserStatus` :
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

    /// <summary>
    /// Étape 1 : crée le compte s'il n'existe pas, envoie le code, et NE DIT RIEN D'AUTRE.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// QUATRE RÉPONSES DEVENUES UNE SEULE.
    ///
    /// Cette route anonyme distinguait quatre issues : adresse inconnue, adresse connue
    /// et éligible (avec son Guid), 409 « déjà vendeur », 403 « non éligible ». On n'y
    /// apprenait pas seulement qu'un compte existe : on apprenait QUE C'EST UN VENDEUR,
    /// et QU'IL EST SUSPENDU. De la donnée concurrentielle et personnelle, servie sans
    /// authentification, à qui voulait boucler sur une liste d'adresses.
    ///
    /// Elle répond désormais la MÊME CHOSE dans tous les cas. Les refus 409 et 403
    /// existent toujours — ils ont été déplacés dans `/verify`, DERRIÈRE le code reçu par
    /// e-mail. Le vendeur légitime les lit, parce qu'il relève sa boîte ; celui qui
    /// sonde ne les atteint jamais.
    ///
    /// ON ENVOIE `RegisterUserCommand` D'ABORD, MÊME QUAND LE COMPTE EXISTE PEUT-ÊTRE.
    ///
    /// C'était l'inverse — lecture par adresse, puis branche — et cet ordre-là rouvrait
    /// l'oracle par la porte de service : la validation d'entrée (mot de passe trop
    /// court, nom vide) ne s'exécutait que dans la branche « adresse inconnue ». Il
    /// suffisait donc de soumettre un mot de passe volontairement invalide : une erreur
    /// de validation signifiait « adresse libre », une réponse uniforme « adresse prise ».
    /// En passant systématiquement par la commande, le pipeline de validation s'exécute
    /// dans les deux cas, et les mêmes entrées produisent les mêmes erreurs.
    ///
    /// CONSÉQUENCE POUR L'APP : LE TÉLÉPHONE DEVIENT OBLIGATOIRE DANS LES DEUX CAS.
    /// `RegisterUserCommandValidator` l'exige, et la commande part maintenant même quand
    /// le compte existe déjà. Un formulaire qui omettait le numéro pour un acheteur qui
    /// se lance recevra une erreur de validation là où il passait avant.
    ///
    /// `identity.user.phone_taken` EST AVALÉ COMME LE RESTE, ET C'EST UN ARBITRAGE.
    /// Le dire renseignerait sur l'existence d'un compte derrière un numéro — même faille,
    /// autre identifiant. Le taire laisse un inscrit légitime attendre un code qui ne
    /// viendra pas, d'où la dernière phrase du message. Si le support voit passer ce cas,
    /// c'est un arbitrage à revoir, pas un bug.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        SellerSelfRegisterRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        var created = await sender.Send(new RegisterUserCommand(
            request.FirstName, request.LastName, request.Email,
            request.PhoneNumber ?? string.Empty, request.Password, CreatedByAdmin: false), ct);

        if (created.IsSuccess)
        {
            // Compte neuf : `RegisterUserCommand` a déjà publié
            // `EmailVerificationRequestedIntegrationEvent`, donc le code part tout seul.
            return UniformRegisterResponse();
        }

        // Une erreur qui ne parle PAS de l'existence d'un compte parle de la saisie —
        // mot de passe trop court, e-mail malformé, nom vide. Celle-là se rend telle
        // quelle : elle porte sur ce que l'appelant vient de taper, pas sur ce que la
        // base contient, et la taire empêcherait de corriger un formulaire.
        if (!IsExistingAccountConflict(created.Error.Code))
        {
            return created.Match(_ => Results.Ok());
        }

        // À partir d'ici le compte existe. Tout ce qui suit est SILENCIEUX : on décide
        // seulement s'il y a lieu d'envoyer un code, jamais ce qu'on répond.
        var existing = await identity.GetUserByEmailAsync(request.Email, ct);

        if (existing is not null && IsEligibleForOnboarding(existing.Status))
        {
            var shop = await sellers.GetSellerByUserIdAsync(existing.Id, ct);
            if (shop is null)
            {
                // Un acheteur qui se lance : on émet un code pour PROUVER qu'il relève
                // bien cette boîte avant de lui rattacher une boutique. Sans cette
                // preuve, n'importe qui rattacherait une boutique au compte d'autrui.
                _ = await sender.Send(new RequestEmailVerificationCommand(existing.Id), ct);
            }
        }

        // Compte suspendu, ou déjà rattaché à une boutique : AUCUN e-mail. Inviter à
        // poursuivre un parcours qui se refermera de toute façon n'aide personne, et
        // chaque envoi déclenché par un inconnu est un e-mail de trop dans la boîte de
        // la victime.
        return UniformRegisterResponse();
    }

    /// <summary>
    /// La réponse unique de `/register`. Elle ne varie sur RIEN — ni corps, ni code HTTP.
    /// Le message couvre les trois issues sans en désigner aucune.
    /// </summary>
    private static IResult UniformRegisterResponse()
        => Results.Ok(new SellerRegisterResponse(
            Registered: true,
            Message: "Si cette adresse peut ouvrir une boutique, un code à 6 chiffres vient "
                   + "d'être envoyé. Si vous avez déjà une boutique, connectez-vous. Sans code "
                   + "d'ici quelques minutes, vérifiez l'adresse saisie ou contactez l'assistance."));

    /// <summary>
    /// Ce code d'erreur signifie-t-il « ce compte existe déjà » ?
    ///
    /// Les deux valeurs viennent de `RegisterUserCommandHandler`. Elles sont listées
    /// explicitement plutôt que déduites du préfixe : le jour où une troisième erreur de
    /// conflit apparaîtra, il vaut mieux qu'elle remonte à l'appelant — visible, donc
    /// corrigeable — plutôt qu'elle disparaisse dans la réponse uniforme.
    /// </summary>
    private static bool IsExistingAccountConflict(string code)
        => code is "identity.user.email_taken" or "identity.user.phone_taken";

    /// <summary>
    /// Étape 2 : valide le code, PUIS contrôle l'éligibilité, puis crée la boutique.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// L'ORDRE A CHANGÉ, ET IL EST DÉLIBÉRÉ.
    ///
    /// Le contrôle de statut passait AVANT la vérification du code. Il gardait bien le
    /// rôle et l'activation — c'était son rôle — mais il répondait 403 à un appelant qui
    /// n'avait rien prouvé, donc il renseignait. Il s'exécute maintenant APRÈS : le
    /// refus est identique, il est simplement réservé à qui a prouvé qu'il relève la
    /// boîte.
    ///
    /// CE QUI N'A PAS CHANGÉ : le contrôle est toujours là, toujours avant
    /// `AssignRoleCommand` et `ApproveUserCommand`. C'est ce qui compte, parce
    /// qu'`ApproveUserCommand` fait passer un compte à `Active` DEPUIS N'IMPORTE QUEL
    /// état, suspension comprise. Sans cette barrière, un vendeur suspendu pour fraude
    /// qui garde l'accès à sa boîte mail se réactivait tout seul en deux requêtes
    /// anonymes — nouveau rôle, nouvelle boutique, aucun KYB.
    ///
    /// L'IDENTIFIANT N'EST PLUS DANS LE CORPS. `/verify` prenait un Guid, donc il était
    /// attaquable avec un Guid récolté ailleurs, muni d'un code obtenu avant la
    /// suspension. Il prend l'adresse : il faut désormais les deux, et les deux
    /// viennent de la même boîte.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> VerifyAsync(
        SellerVerifyRequest request,
        ISender sender,
        IIdentityModuleApi identity,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        // 1. Code (strict : preuve de possession de la boîte). Adresse inconnue et code
        //    faux renvoient la MÊME erreur — voir VerifyEmailCodeCommandHandler.
        var verified = await sender.Send(new VerifyEmailCodeCommand(request.Email, request.Code), ct);
        if (verified.IsFailure)
        {
            return verified.Match(() => Results.Ok());
        }

        // 2. Le compte est prouvé : on peut le nommer, et donc refuser en clair.
        var account = await identity.GetUserByEmailAsync(request.Email, ct);
        if (account is null || !IsEligibleForOnboarding(account.Status))
        {
            return Results.Problem(
                title: "identity.user.not_eligible",
                detail: "Ce compte ne peut pas ouvrir de boutique. Contactez l'assistance.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var userId = account.Id;

        var existingShop = await sellers.GetSellerByUserIdAsync(userId, ct);
        if (existingShop is not null)
        {
            return Results.Problem(
                title: "sellers.seller.already_seller",
                detail: "Ce compte est déjà rattaché à une boutique.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // 3. Boutique AVANT rôle/activation : c'est l'étape qui peut légitimement
        //    échouer (nom de boutique déjà pris). L'e-mail est désormais vérifié,
        //    donc RegisterSeller l'accepte.
        var metadata = request.Company?.ToDomain();
        var sellerResult = await sender.Send(
            new RegisterSellerCommand(userId, request.ShopName, 0.10m, metadata), ct);
        if (sellerResult.IsFailure)
        {
            return sellerResult.Match(_ => Results.Ok());
        }

        var sellerId = sellerResult.Value;

        // 4. Rôle Seller — sans lui, impossible de se connecter à /seller/*.
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

        var roleResult = await sender.Send(new AssignRoleCommand(userId, sellerRole.Id), ct);
        if (roleResult.IsFailure)
        {
            return roleResult.Match(() => Results.Ok());
        }

        // 5. Activation du compte : l'e-mail est prouvé, le vendeur doit pouvoir
        //    entrer et compléter son KYB. La publication produit reste gatée ailleurs.
        var approveResult = await sender.Send(new ApproveUserCommand(userId), ct);
        if (approveResult.IsFailure)
        {
            return approveResult.Match(() => Results.Ok());
        }

        return Results.Ok(new SellerVerifyResponse(sellerId, userId));
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
    /// Corps de l'étape 2. L'ADRESSE, pas l'identifiant — voir VerifyEmailCodeCommand.
    /// Le nom de boutique et les infos société sont fournis ici, une fois le code en
    /// main : la boutique n'existe qu'après validation.
    /// </summary>
    public sealed record SellerVerifyRequest(
        string Email,
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

    /// <summary>
    /// Réponse de `/register`. NI IDENTIFIANT, NI INDICATION D'EXISTENCE.
    ///
    /// `IsNewAccount` a disparu avec `UserId` : c'était le même aveu sous un autre nom.
    /// Le champ `Registered` ne dit rien non plus — il vaut toujours vrai. Il n'est là
    /// que pour donner une forme stable à l'app.
    /// </summary>
    public sealed record SellerRegisterResponse(bool Registered, string Message)
    {
        /// <summary>Toujours vrai : l'app doit ensuite demander le code puis /verify.</summary>
        public bool RequiresVerification => true;
    }

    public sealed record SellerVerifyResponse(Guid SellerId, Guid UserId);

    /// <summary>Demande de réinitialisation : juste l'e-mail.</summary>
    public sealed record SellerResendRequest(string Email);

    /// <summary>
    /// Réponse CONSTANTE : ni identifiant, ni indication d'existence. Voir ResendCodeAsync.
    /// </summary>
    public sealed record SellerResendResponse(bool Sent, string Message);

    public sealed record SellerForgotRequest(string Email);

    /// <summary>Réinitialisation : e-mail + code reçu + nouveau mot de passe.</summary>
    public sealed record SellerResetRequest(string Email, string Code, string NewPassword);
}
