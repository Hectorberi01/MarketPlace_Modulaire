using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Mfa;

/// <summary>Vérifie le code TOTP contre le secret en attente puis active la MFA.</summary>
internal sealed class ConfirmMfaCommandHandler : ICommandHandler<ConfirmMfaCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ITotpService _totpService;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ConfirmMfaCommandHandler(
        IUserRepository userRepository,
        ITotpService totpService,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _totpService = totpService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        if (user.MfaSecret is null)
        {
            return Result.Failure(Error.Conflict("identity.user.mfa_not_initiated", "Aucune activation MFA initiée."));
        }

        if (!_totpService.VerifyCode(user.MfaSecret, command.Code))
        {
            return Result.Failure(Error.Unauthorized("identity.auth.mfa_invalid", "Code de double authentification invalide."));
        }

        var result = user.ConfirmMfaSetup();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
