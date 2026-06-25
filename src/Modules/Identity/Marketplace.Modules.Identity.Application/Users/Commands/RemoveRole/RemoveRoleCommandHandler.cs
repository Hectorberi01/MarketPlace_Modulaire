using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RemoveRole;

/// <summary>Retire le rôle indiqué du compte.</summary>
internal sealed class RemoveRoleCommandHandler : ICommandHandler<RemoveRoleCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RemoveRoleCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        user.RemoveRole(command.RoleId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
