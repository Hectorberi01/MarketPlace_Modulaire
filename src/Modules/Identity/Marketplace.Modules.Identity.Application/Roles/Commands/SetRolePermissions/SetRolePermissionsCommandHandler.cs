using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.SetRolePermissions;

/// <summary>Remplace les permissions du rôle (chaque code est validé par le VO Permission).</summary>
internal sealed class SetRolePermissionsCommandHandler : ICommandHandler<SetRolePermissionsCommand>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public SetRolePermissionsCommandHandler(IRoleRepository roleRepository, IIdentityUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(new RoleId(command.RoleId), cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("identity.role.not_found", $"Rôle {command.RoleId} introuvable."));
        }

        var result = role.SetPermissions(command.Permissions);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
