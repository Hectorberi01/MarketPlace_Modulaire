using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>Hash le jeton fourni et délègue la confirmation à l'agrégat User.</summary>
internal sealed class ConfirmEmailCommandHandler : ICommandHandler<ConfirmEmailCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ConfirmEmailCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var providedHash = _tokenGenerator.Hash(command.Token);

        var result = user.ConfirmEmail(providedHash, DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
