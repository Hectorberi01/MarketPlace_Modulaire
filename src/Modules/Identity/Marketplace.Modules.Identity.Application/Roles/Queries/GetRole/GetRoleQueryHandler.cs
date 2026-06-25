using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Application.Roles.Queries.GetRole;

internal sealed class GetRoleQueryHandler : IQueryHandler<GetRoleQuery, RoleSummary>
{
    private readonly IRoleRepository _roleRepository;

    public GetRoleQueryHandler(IRoleRepository roleRepository)
        => _roleRepository = roleRepository;

    public async Task<Result<RoleSummary>> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(new RoleId(query.RoleId), cancellationToken);
        if (role is null)
        {
            return Error.NotFound("identity.role.not_found", $"Rôle {query.RoleId} introuvable.");
        }

        return new RoleSummary(role.Id.Value, role.Name, role.Description, role.IsSystem, role.Permissions.ToList());
    }
}
