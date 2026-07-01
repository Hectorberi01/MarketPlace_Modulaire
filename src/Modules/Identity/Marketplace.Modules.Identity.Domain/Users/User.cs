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
    public bool MfaEnabled { get; private set; }
    public string? MfaSecret { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationExpiresOnUtc { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<UserRoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <summary>Identifiants des rôles assignés (raccourci de lecture).</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleAssignments.Select(a => a.RoleId).ToList().AsReadOnly();

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

    /// <summary>Confirme l'e-mail à partir du hash du token fourni ; active le compte.</summary>
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
        if (Status == UserStatus.PendingVerification)
        {
            Status = UserStatus.Active;
        }

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

    public Result Suspend()
    {
        Status = UserStatus.Suspended;
        RevokeAllRefreshTokens();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (!EmailVerified)
        {
            return Result.Failure(Error.Conflict("identity.user.email_unverified", "L'e-mail doit être vérifié avant réactivation."));
        }

        Status = UserStatus.Active;
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
