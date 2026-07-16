using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Users.Events;

namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>
/// Compte d'un acteur (acheteur, vendeur, admin). Source d'identité pour tout le
/// système (cf. dossier, module Identity). Agrégat racine : possède ses rôles
/// assignés et ses refresh tokens.
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    private readonly List<UserRoleAssignment> _roleAssignments = new();
    private readonly List<RefreshToken> _refreshTokens = new();

    private User()
    {
    }

    private User(
        UserId id,
        string firstName,
        string lastName,
        Email email,
        PhoneNumber phoneNumber,
        string passwordHash,
        string emailVerificationTokenHash,
        DateTime emailVerificationExpiresOnUtc)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        PasswordHash = passwordHash;
        Status = UserStatus.PendingVerification;
        EmailVerified = false;
        MfaEnabled = false;
        SecurityStamp = Guid.NewGuid();
        EmailVerificationTokenHash = emailVerificationTokenHash;
        EmailVerificationExpiresOnUtc = emailVerificationExpiresOnUtc;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new UserRegisteredDomainEvent(id.Value, email.Value, firstName));
    }

    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public PhoneNumber PhoneNumber { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserStatus Status { get; private set; }
    public bool EmailVerified { get; private set; }

    /// <summary>
    /// Renseigné si l'e-mail a été marqué vérifié PAR UN ADMINISTRATEUR, et non par le
    /// titulaire cliquant sur un lien reçu dans sa boîte.
    ///
    /// Les deux mettent <see cref="EmailVerified"/> à vrai, mais ils ne valent pas la
    /// même chose. Cliquer sur un lien PROUVE qu'on relève cette adresse. Un
    /// administrateur, lui, ATTESTE — sur la foi d'un échange, d'un document, d'un appel.
    /// C'est légitime, et c'est aujourd'hui le seul chemin possible faute de service
    /// d'e-mailing. Mais c'est une confiance, pas une preuve.
    ///
    /// Sans cette colonne, les deux seraient indiscernables. Le jour où un envoi
    /// d'e-mails sera branché, on ne saurait plus dire quelles adresses ont réellement
    /// été confirmées — et on n'aurait aucun moyen de demander aux autres de le faire.
    /// Nulle = vérification authentique (ou pas de vérification du tout).
    /// </summary>
    public DateTime? EmailVerifiedByAdminOnUtc { get; private set; }

    public bool MfaEnabled { get; private set; }
    public string? MfaSecret { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationExpiresOnUtc { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>
    /// Date de suppression du compte (anonymisation), à la demande du titulaire.
    ///
    /// Null pour tout compte vivant. C'est la SEULE trace temporelle qui subsiste : elle
    /// permet de prouver, en cas de réclamation, que la demande a bien été honorée — et
    /// quand. Sans elle, vous auriez effacé les données ET la preuve de les avoir
    /// effacées.
    /// </summary>
    public DateTime? DeletedOnUtc { get; private set; }

    /// <summary>
    /// Version des conditions générales acceptée par l'utilisateur, et date de
    /// cette acceptation. Null tant qu'il n'a rien accepté.
    ///
    /// Le consentement se PROUVE, sinon il n'existe pas. Le Code du numérique fait
    /// dépendre de l'acceptation des conditions la validité du contrat conclu en
    /// ligne : le jour d'un litige, il faut pouvoir dire QUI a accepté QUOI et
    /// QUAND. Un drapeau stocké dans le téléphone ne prouve rien — il disparaît à
    /// la réinstallation et se modifie sur un appareil rooté. La trace vit donc ici,
    /// côté serveur, sur l'agrégat User.
    ///
    /// On garde la VERSION, pas un simple booléen : le jour où les conditions
    /// changent, il faut savoir qui a accepté l'ancienne rédaction — et pouvoir
    /// redemander l'accord à ceux-là seulement.
    /// </summary>
    public string? AcceptedTermsVersion { get; private set; }

    public DateTime? AcceptedTermsOnUtc { get; private set; }

    public IReadOnlyCollection<UserRoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <summary>Identifiants des rôles assignés (raccourci de lecture).</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleAssignments.Select(a => a.RoleId).ToList().AsReadOnly();

    /// <summary>
    /// Enregistre l'acceptation d'une version des conditions générales.
    ///
    /// IDEMPOTENT : ré-accepter la version déjà acceptée ne change RIEN, pas même
    /// la date. Sinon, un client qui rejoue l'appel effacerait la date du
    /// consentement d'origine — précisément la donnée qu'on cherche à conserver.
    /// </summary>
    public Result AcceptTerms(string version, DateTime onUtc)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            // `Result` (non générique) n'a pas de conversion implicite depuis Error :
            // seul `Result<T>` en a une. On passe donc par Result.Failure.
            return Result.Failure(
                Error.Validation("identity.user.terms_version_required", "La version des conditions est obligatoire."));
        }

        var accepted = version.Trim();
        if (AcceptedTermsVersion == accepted)
        {
            return Result.Success();
        }

        AcceptedTermsVersion = accepted;
        AcceptedTermsOnUtc = onUtc;
        return Result.Success();
    }

    /// <summary>
    /// Crée un compte en attente de vérification. Le hash du mot de passe et le
    /// hash du token de vérification sont calculés par l'Application (services
    /// d'infrastructure) ; le domaine ne voit jamais les secrets en clair.
    /// </summary>
    public static Result<User> Register(
        string firstName,
        string lastName,
        Email email,
        PhoneNumber phoneNumber,
        string passwordHash,
        string emailVerificationTokenHash,
        DateTime emailVerificationExpiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return Error.Validation("identity.user.first_name_required", "Le prénom est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Error.Validation("identity.user.last_name_required", "Le nom est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire.");
        }

        return new User(
            UserId.New(),
            firstName.Trim(),
            lastName.Trim(),
            email,
            phoneNumber,
            passwordHash,
            emailVerificationTokenHash,
            emailVerificationExpiresOnUtc);
    }

    /// <summary>
    /// Confirme l'e-mail à partir du hash du token reçu par lien.
    ///
    /// ⚠️ N'ACTIVE PLUS LE COMPTE. Prouver qu'on relève bien cette boîte aux lettres
    /// n'est pas la même chose qu'être autorisé à entrer — c'est l'administrateur qui
    /// décide, via <see cref="Approve"/>.
    ///
    /// Cette méthode est aujourd'hui dormante : aucun service d'e-mailing n'est
    /// branché, donc personne ne reçoit de lien. Elle a néanmoins été corrigée
    /// maintenant, et non « le jour venu » : si elle avait gardé son `Status = Active`,
    /// le premier développeur qui câblera un envoi d'e-mails rendrait l'approbation
    /// administrateur contournable — sans le savoir, et sans que rien ne le signale.
    /// </summary>
    public Result ConfirmEmail(string providedTokenHash, DateTime nowUtc)
    {
        if (EmailVerified)
        {
            return Result.Success();
        }

        if (EmailVerificationTokenHash is null || EmailVerificationExpiresOnUtc is null)
        {
            return Result.Failure(Error.Conflict("identity.user.no_verification_pending", "Aucune vérification d'e-mail en attente."));
        }

        if (EmailVerificationExpiresOnUtc < nowUtc)
        {
            return Result.Failure(Error.Validation("identity.user.verification_expired", "Le lien de vérification a expiré."));
        }

        if (!FixedTimeEquals(EmailVerificationTokenHash, providedTokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.verification_invalid", "Lien de vérification invalide."));
        }

        EmailVerified = true;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;

        // Aucune bascule de `Status` ici : voir le commentaire de la méthode.
        // L'e-mail vérifié est un FAIT ; l'activation est une DÉCISION.

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    /// <summary>
    /// Un administrateur atteste que l'adresse appartient bien au titulaire.
    ///
    /// À distinguer de <see cref="ConfirmEmail"/>, juste au-dessus : là, le titulaire
    /// cliquait un lien reçu dans sa boîte — c'était une PREUVE. Ici, un humain se porte
    /// garant sur la foi d'un échange, d'un document, d'un appel. C'est une CONFIANCE.
    ///
    /// La distinction est conservée dans <see cref="EmailVerifiedByAdminOnUtc"/>. Ne
    /// pas la conserver reviendrait à effacer, dans les données, la différence entre
    /// « prouvé » et « cru sur parole » — et personne, plus tard, ne pourrait la
    /// reconstituer.
    ///
    /// Le jeton de vérification en attente est purgé : il n'a plus d'objet, et un jeton
    /// qui traîne est un jeton qu'on peut rejouer.
    ///
    /// Idempotente. N'active PAS le compte : l'activation reste une décision distincte
    /// (<see cref="Approve"/>). Vérifier une adresse et autoriser l'accès sont deux
    /// choses différentes ; les confondre a déjà coûté assez cher dans ce module.
    /// </summary>
    public Result MarkEmailVerifiedByAdmin(DateTime nowUtc)
    {
        if (EmailVerified)
        {
            return Result.Success();
        }

        EmailVerified = true;
        EmailVerifiedByAdminOnUtc = nowUtc;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    public Result UpdateProfile(string firstName, string lastName, PhoneNumber phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return Result.Failure(Error.Validation("identity.user.first_name_required", "Le prénom est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Result.Failure(Error.Validation("identity.user.last_name_required", "Le nom est obligatoire."));
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber;
        return Result.Success();
    }

    /// <summary>Change le mot de passe : nouvelle empreinte, rotation du security stamp, révocation des refresh tokens.</summary>
    public Result ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            return Result.Failure(Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire."));
        }

        PasswordHash = newPasswordHash;
        RegenerateSecurityStamp();
        RevokeAllRefreshTokens();

        Raise(new UserPasswordChangedDomainEvent(Id.Value));
        return Result.Success();
    }

    /// <summary>Initie une réinitialisation de mot de passe : stocke le hash du jeton + son expiration.</summary>
    public Result BeginPasswordReset(string tokenHash, DateTime expiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.reset_token_required", "Jeton de réinitialisation manquant."));
        }

        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresOnUtc = expiresOnUtc;
        return Result.Success();
    }

    /// <summary>Réinitialise le mot de passe à partir du hash du jeton fourni (usage unique).</summary>
    public Result ResetPassword(string providedTokenHash, string newPasswordHash, DateTime nowUtc)
    {
        if (PasswordResetTokenHash is null || PasswordResetExpiresOnUtc is null)
        {
            return Result.Failure(Error.Conflict("identity.user.no_reset_pending", "Aucune réinitialisation en attente."));
        }

        if (PasswordResetExpiresOnUtc < nowUtc)
        {
            return Result.Failure(Error.Validation("identity.user.reset_expired", "Le lien de réinitialisation a expiré."));
        }

        if (!FixedTimeEquals(PasswordResetTokenHash, providedTokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.reset_invalid", "Lien de réinitialisation invalide."));
        }

        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            return Result.Failure(Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire."));
        }

        PasswordHash = newPasswordHash;
        PasswordResetTokenHash = null;
        PasswordResetExpiresOnUtc = null;
        RegenerateSecurityStamp();
        RevokeAllRefreshTokens();
        Raise(new UserPasswordChangedDomainEvent(Id.Value));
        return Result.Success();
    }

    public Result AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("identity.user.role_required", "Le rôle est obligatoire."));
        }

        if (_roleAssignments.All(a => a.RoleId != roleId))
        {
            _roleAssignments.Add(new UserRoleAssignment(Guid.NewGuid(), roleId));
            Raise(new UserRoleAssignedDomainEvent(Id.Value, roleId));
        }

        return Result.Success();
    }

    public Result RemoveRole(Guid roleId)
    {
        var assignment = _roleAssignments.FirstOrDefault(a => a.RoleId == roleId);
        if (assignment is not null)
        {
            _roleAssignments.Remove(assignment);
        }

        return Result.Success();
    }

    /// <summary>
    /// Approbation par un administrateur : le compte peut désormais se connecter.
    ///
    /// ⚠️ Cette méthode ne touche PAS <see cref="EmailVerified"/>, et c'est
    /// délibéré. Un administrateur qui valide un compte ne prouve pas que l'adresse
    /// e-mail appartient bien à cette personne — il atteste que le compte est
    /// légitime, ce qui n'est pas la même chose. Écrire `EmailVerified = true` ici
    /// fabriquerait un fait qui n'a jamais eu lieu ; le jour où un service d'envoi
    /// d'e-mails sera branché, on ne saurait plus distinguer les adresses réellement
    /// confirmées de celles qu'un admin a laissé passer.
    ///
    /// C'est <see cref="Status"/> qui gouverne l'accès. <see cref="EmailVerified"/>
    /// reste un constat : aujourd'hui faux pour tout le monde, faute d'e-mailing.
    ///
    /// Idempotente : approuver deux fois n'est pas une erreur (double-clic, rejeu).
    /// </summary>
    public Result Approve()
    {
        if (Status == UserStatus.Active)
        {
            return Result.Success();
        }

        Status = UserStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// Refus ou sanction. Réversible : rien n'est effacé, et la trace de la
    /// tentative d'inscription reste consultable — c'est justement ce qu'on veut
    /// conserver d'un compte frauduleux.
    ///
    /// Révoque les jetons : un compte suspendu dont la session reste ouverte n'est
    /// pas suspendu.
    /// </summary>
    public Result Suspend()
    {
        Status = UserStatus.Suspended;
        RevokeAllRefreshTokens();
        return Result.Success();
    }

    /// <summary>
    /// Levée de la suspension.
    ///
    /// L'ancienne garde exigeait <c>EmailVerified</c>. Elle ne tenait que parce que
    /// l'inscription trichait — elle appelait `ConfirmEmail` d'office, faute de
    /// service d'e-mailing. Maintenant que cette triche est retirée, la garde aurait
    /// rendu TOUTE réactivation impossible : plus personne n'a l'e-mail vérifié.
    ///
    /// Le bon critère est le statut, pas l'e-mail.
    /// </summary>
    public Result Reactivate()
    {
        if (Status != UserStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "identity.user.not_suspended", "Ce compte n'est pas suspendu."));
        }

        Status = UserStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// SUPPRESSION DU COMPTE à la demande de son titulaire — par anonymisation.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI ON N'EFFACE PAS LA LIGNE
    ///
    /// Les commandes doivent être conservées pour la comptabilité et le fisc, plusieurs
    /// années durant. Chacune référence son acheteur. Supprimer la ligne « utilisateur »
    /// briserait ces références et rendrait vos livres incohérents — donc inexploitables
    /// en cas de contrôle.
    ///
    /// Ce que la loi et Apple exigent, ce n'est pas la disparition de la LIGNE : c'est
    /// celle des DONNÉES PERSONNELLES. On remplace donc tout ce qui identifie la
    /// personne, et l'on ne garde qu'une coquille comptable qui ne désigne plus
    /// personne.
    ///
    /// CE QUI DISPARAÎT : nom, prénom, e-mail, téléphone, mot de passe, secret MFA,
    /// jetons de session et de réinitialisation.
    /// CE QUI RESTE : l'identifiant technique et la date de création — le strict
    /// nécessaire pour que les commandes passées restent rattachables à « un » acheteur.
    ///
    /// ⚠️ IRRÉVERSIBLE, et sans retour possible : les données d'origine n'existent plus.
    /// Aucune méthode ne fait sortir de l'état Deleted, et c'est délibéré.
    ///
    /// ⚠️ L'E-MAIL ANONYME EST DÉRIVÉ DE L'IDENTIFIANT, car la colonne est UNIQUE. Deux
    /// suppressions ne doivent pas entrer en collision. Le téléphone, lui, est identique
    /// pour tous les comptes supprimés : son index unique est FILTRÉ pour les exclure
    /// (voir UserConfiguration). Sans ce filtre, la deuxième suppression échouerait —
    /// et l'utilisateur aurait reçu une erreur sans comprendre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Anonymize(DateTime nowUtc)
    {
        if (Status == UserStatus.Deleted)
        {
            // Idempotent : un double appel (rejeu, double tap) ne doit pas échouer.
            // Il n'y a d'ailleurs plus rien à anonymiser.
            return Result.Success();
        }

        // L'e-mail doit rester un e-mail valide (le value object le vérifie) ET rester
        // unique. On le dérive donc de l'identifiant. Le domaine « .invalid » est
        // réservé par l'IETF : il ne peut appartenir à personne, et aucun courrier ne
        // partira jamais vers lui par mégarde.
        var anonymousEmail = Email.Create($"deleted-{Id.Value:N}@deleted.invalid");
        if (anonymousEmail.IsFailure)
        {
            return Result.Failure(anonymousEmail.Error);
        }

        // Le téléphone doit rester au format attendu (8 à 15 chiffres). Même valeur pour
        // tous les comptes supprimés : l'unicité est levée par l'index filtré.
        var anonymousPhone = PhoneNumber.Create("00000000");
        if (anonymousPhone.IsFailure)
        {
            return Result.Failure(anonymousPhone.Error);
        }

        FirstName = "Compte";
        LastName = "supprimé";
        Email = anonymousEmail.Value;
        PhoneNumber = anonymousPhone.Value;

        // Le hachage est REMPLACÉ, pas vidé : une chaîne vide pourrait, selon
        // l'implémentation d'un vérificateur, être considérée comme « pas de mot de
        // passe » — donc acceptée. On y met une valeur qu'aucun mot de passe ne peut
        // produire, et de toute façon la connexion est barrée par le statut.
        PasswordHash = "DELETED";

        MfaEnabled = false;
        MfaSecret = null;

        EmailVerified = false;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;
        PasswordResetTokenHash = null;
        PasswordResetExpiresOnUtc = null;
        EmailVerifiedByAdminOnUtc = null;

        Status = UserStatus.Deleted;
        DeletedOnUtc = nowUtc;

        // Les jetons de RAFRAÎCHISSEMENT sont révoqués : plus aucune session ne peut
        // être renouvelée, sur aucun appareil.
        RevokeAllRefreshTokens();

        // ⚠️ HONNÊTETÉ SUR CE QUI N'EST PAS FAIT.
        //
        // Les jetons d'ACCÈS déjà émis restent techniquement valides jusqu'à leur
        // expiration (quelques dizaines de minutes) : ils sont vérifiés par signature,
        // sans relecture en base. Le `SecurityStamp` ci-dessous existe pour cela — mais
        // il n'est aujourd'hui **jamais lu** à la validation (vérifié : il n'apparaît
        // que comme colonne). Le changer ne révoque donc RIEN pour l'instant.
        //
        // On le fait tourner quand même, pour que le jour où la validation le
        // consultera, les comptes supprimés soient couverts sans y repenser.
        //
        // La fenêtre résiduelle est bornée par la durée de vie du jeton d'accès, et
        // elle se referme d'elle-même : aucun renouvellement n'est possible.
        SecurityStamp = Guid.NewGuid();

        return Result.Success();
    }

    // ----------------------------------------------------------------- Refresh

    public RefreshToken IssueRefreshToken(string tokenHash, DateTime expiresOnUtc)
    {
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, expiresOnUtc);
        _refreshTokens.Add(token);
        return token;
    }

    public RefreshToken? FindActiveRefreshToken(string tokenHash, DateTime nowUtc)
        => _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive(nowUtc));

    public Result RevokeRefreshToken(string tokenHash)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        token?.Revoke();
        return Result.Success();
    }

    public void RevokeAllRefreshTokens()
    {
        foreach (var token in _refreshTokens.Where(t => t.RevokedOnUtc is null))
        {
            token.Revoke();
        }
    }

    // --------------------------------------------------------------------- MFA

    /// <summary>Initie l'activation MFA : stocke le secret TOTP, encore non confirmé.</summary>
    public Result BeginMfaSetup(string secret)
    {
        if (MfaEnabled)
        {
            return Result.Failure(Error.Conflict("identity.user.mfa_already_enabled", "La double authentification est déjà active."));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.Failure(Error.Validation("identity.user.mfa_secret_required", "Secret MFA manquant."));
        }

        MfaSecret = secret;
        return Result.Success();
    }

    /// <summary>Confirme l'activation MFA après vérification d'un code TOTP par l'Application.</summary>
    public Result ConfirmMfaSetup()
    {
        if (string.IsNullOrWhiteSpace(MfaSecret))
        {
            return Result.Failure(Error.Conflict("identity.user.mfa_not_initiated", "Aucune activation MFA initiée."));
        }

        MfaEnabled = true;
        RegenerateSecurityStamp();
        return Result.Success();
    }

    public Result DisableMfa()
    {
        MfaEnabled = false;
        MfaSecret = null;
        RegenerateSecurityStamp();
        return Result.Success();
    }

    private void RegenerateSecurityStamp() => SecurityStamp = Guid.NewGuid();

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
