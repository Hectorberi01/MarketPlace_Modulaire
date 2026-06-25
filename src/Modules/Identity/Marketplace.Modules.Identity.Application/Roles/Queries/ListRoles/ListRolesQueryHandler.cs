using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;

internal sealed class ListRolesQueryHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleSummary>>
{
    private readonly IRoleRepository _roleRepository;

    public ListRolesQueryHandler(IRoleRepository roleRepository)
        => _roleRepository = roleRepository;

    public async Task<Result<IReadOnlyList<RoleSummary>>> Handle(ListRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.ListAsync(cancellationToken);

        IReadOnlyList<RoleSummary> summaries = roles
            .Select(r => new RoleSummary(r.Id.Value, r.Name, r.Description, r.IsSystem, r.Permissions.ToList()))
            .ToList();

        return Result.Success(summaries);
    }
}
