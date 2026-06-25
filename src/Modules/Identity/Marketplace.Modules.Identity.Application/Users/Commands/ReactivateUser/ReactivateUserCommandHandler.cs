using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ReactivateUser;

/// <summary>Réactive le compte via la transition métier de l'agrégat.</summary>
internal sealed class ReactivateUserCommandHandler : ICommandHandler<ReactivateUserCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ReactivateUserCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var result = user.Reactivate();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
