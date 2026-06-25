using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;

/// <summary>Vérifie l'existence du rôle puis l'assigne à l'utilisateur.</summary>
internal sealed class AssignRoleCommandHandler : ICommandHandler<AssignRoleCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var role = await _roleRepository.GetByIdAsync(new RoleId(command.RoleId), cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("identity.role.not_found", $"Rôle {command.RoleId} introuvable."));
        }

        var result = user.AssignRole(role.Id.Value);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
