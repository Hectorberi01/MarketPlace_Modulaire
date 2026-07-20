using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;

/// <summary>
/// Hashe le code fourni et le compare au code en attente (comparaison à temps
/// constant, côté domaine). Succès = e-mail marqué vérifié et code purgé.
/// </summary>
internal sealed class VerifyEmailCodeCommandHandler : ICommandHandler<VerifyEmailCodeCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public VerifyEmailCodeCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(VerifyEmailCodeCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var providedHash = _tokenGenerator.Hash(command.Code);
        var result = user.ConsumeEmailVerificationCode(providedHash, DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        // La confirmation de l'e-mail ACTIVE le compte en libre-service : c'est
        // l'instant où l'on sait que l'adresse est réelle. Tant qu'il n'a pas
        // confirmé, le compte reste PendingVerification et ne peut pas se connecter.
        // Idempotent : Approve() n'est appelé que sur un compte encore en attente.
        if (user.Status == UserStatus.PendingVerification)
        {
            user.Approve();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
