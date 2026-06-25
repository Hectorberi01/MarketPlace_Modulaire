using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.UpdateRole;

/// <summary>Charge le rôle, vérifie l'unicité du nom si changé, met à jour puis persiste.</summary>
internal sealed class UpdateRoleCommandHandler : ICommandHandler<UpdateRoleCommand>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public UpdateRoleCommandHandler(IRoleRepository roleRepository, IIdentityUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(new RoleId(command.RoleId), cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("identity.role.not_found", $"Rôle {command.RoleId} introuvable."));
        }

        var newName = command.Name.Trim();
        if (!string.Equals(newName, role.Name, StringComparison.OrdinalIgnoreCase)
            && await _roleRepository.NameExistsAsync(newName, cancellationToken))
        {
            return Result.Failure(Error.Conflict("identity.role.name_taken", $"Le rôle « {newName} » existe déjà."));
        }

        var result = role.Update(command.Name, command.Description);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
