using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Contracts;

namespace Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;

/// <summary>Liste tous les rôles (back-office d'administration).</summary>
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;
