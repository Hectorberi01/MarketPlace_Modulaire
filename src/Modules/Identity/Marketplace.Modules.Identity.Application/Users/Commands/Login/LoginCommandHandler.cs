using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Models;
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

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        var user = await _userRepository.GetByEmailAsync(emailResult.Value.Value, cancellationToken);
        if (user is null || !_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        if (user.Status == UserStatus.Suspended)
        {
            return Result.Failure<LoginResponse>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        if (!user.EmailVerified)
        {
            return Result.Failure<LoginResponse>(Error.Forbidden("identity.auth.email_unverified", "L'e-mail n'est pas encore vérifié."));
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.MfaCode))
            {
                return new LoginResponse(MfaRequired: true, Tokens: null);
            }

            if (user.MfaSecret is null || !_totpService.VerifyCode(user.MfaSecret, command.MfaCode))
            {
                return Result.Failure<LoginResponse>(Error.Unauthorized("identity.auth.mfa_invalid", "Code de double authentification invalide."));
            }
        }

        var tokens = await _tokenIssuer.IssueAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse(MfaRequired: false, Tokens: tokens);
    }
}
