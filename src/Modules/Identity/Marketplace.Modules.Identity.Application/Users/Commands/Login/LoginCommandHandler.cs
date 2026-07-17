using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Observability;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Models;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Login;

/// <summary>
/// Vérifie les identifiants et le statut, applique la MFA si activée, puis émet
/// les jetons. Les échecs d'authentification renvoient un message générique pour
/// ne pas révéler quels comptes existent.
/// </summary>
internal sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResponse>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("identity.auth.invalid_credentials", "E-mail ou mot de passe invalide.");

    // Labels de métriques (faible cardinalité, non personnels).
    private const string Method = "password";
    private const string ClientType = "unknown";

    private static readonly Error WrongSurface = Error.Forbidden(
        "identity.auth.wrong_surface",
        "Ce compte n'a pas accès à cette application.");

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _security = security;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        var user = await _userRepository.GetByEmailAsync(emailResult.Value.Value, cancellationToken);

        // ─────────────────────────────────────────────────────────────────────────
        // COMPTE SUPPRIMÉ : on sort AVANT la vérification du mot de passe.
        //
        // Deux raisons, et la première est un vrai risque de plantage :
        //
        //  1. Un compte anonymisé porte `PasswordHash = "DELETED"` — qui n'est PAS un
        //     hachage BCrypt valide. Selon l'implémentation, `Verify` lève une
        //     exception (« salt invalide ») au lieu de renvoyer false. On répondrait
        //     alors 500 à quelqu'un qui tente simplement de se connecter à un compte
        //     qu'il a lui-même supprimé.
        //
        //  2. On renvoie EXACTEMENT la même erreur que pour un mot de passe faux. Dire
        //     « ce compte a été supprimé » confirmerait à un tiers que cette adresse a
        //     existé chez nous — une fuite d'information, offerte sans authentification.
        //
        // L'utilisateur légitime, lui, sait qu'il a supprimé son compte : le message
        // générique ne le désoriente pas.
        // ─────────────────────────────────────────────────────────────────────────
        if (user is not null && user.Status == UserStatus.Deleted)
        {
            _security.LoginFailed(Method, "deleted", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        if (user is null || !_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        if (user.Status == UserStatus.Suspended)
        {
            _security.LoginFailed(Method, "suspended", ClientType);
            return Result.Failure<LoginResponse>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        // Le verrou d'accès est le STATUT, plus `EmailVerified`.
        //
        // L'ancienne garde portait sur `EmailVerified`. Elle ne servait à rien :
        // aucun service d'e-mailing n'étant branché, l'inscription appelait
        // `ConfirmEmail` d'office pour que le compte puisse se connecter. Le
        // contrôle vérifiait donc une valeur que le code venait de poser lui-même
        // trois lignes plus haut.
        //
        // Désormais un compte naît `PendingVerification` et n'en sort que par
        // l'approbation d'un administrateur. `EmailVerified` redevient ce qu'il
        // prétend être : le constat qu'une adresse a été confirmée — aujourd'hui
        // faux pour tout le monde, et honnêtement faux.
        if (user.Status == UserStatus.PendingVerification)
        {
            _security.LoginFailed(Method, "pending_approval", ClientType);
            return Result.Failure<LoginResponse>(Error.Forbidden(
                "identity.auth.pending_approval",
                "Votre compte est en attente de validation. Vous serez prévenu dès qu'il sera activé."));
        }

        // Le compte a-t-il le droit d'entrer sur CETTE surface ?
        //
        // Placé APRÈS la vérification du mot de passe, et volontairement : le
        // message est explicite (« ce compte n'a pas accès à cette application »)
        // plutôt que générique. Cela n'aide pas un attaquant — il faut déjà
        // connaître le mot de passe pour l'obtenir, et l'app acheteur, ouverte à
        // tous, lui aurait de toute façon confirmé que le compte existe. C'est le
        // même arbitrage que les messages « suspendu » et « e-mail non vérifié »
        // juste au-dessus.
        //
        // Placé AVANT la MFA : inutile de faire saisir un code à quelqu'un qu'on
        // s'apprête à refuser.
        if (command.RequiredRoles is { Count: > 0 })
        {
            var roleIds = user.RoleIds.Select(id => new RoleId(id)).ToList();
            var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);
            var names = roles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!command.RequiredRoles.Any(names.Contains))
            {
                _security.LoginFailed(Method, "wrong_surface", ClientType);
                return Result.Failure<LoginResponse>(WrongSurface);
            }
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.MfaCode))
            {
                // Étape intermédiaire (code attendu) : ni succès ni échec.
                return new LoginResponse(MfaRequired: true, Tokens: null);
            }

            if (user.MfaSecret is null || !_totpService.VerifyCode(user.MfaSecret, command.MfaCode))
            {
                _security.LoginFailed("password_mfa", "mfa_invalid", ClientType);
                return Result.Failure<LoginResponse>(Error.Unauthorized("identity.auth.mfa_invalid", "Code de double authentification invalide."));
            }
        }

        var tokens = await _tokenIssuer.IssueAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _security.LoginSuccess(user.MfaEnabled ? "password_mfa" : Method, ClientType);
        return new LoginResponse(MfaRequired: false, Tokens: tokens);
    }
}
