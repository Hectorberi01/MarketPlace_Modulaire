using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Contracts;

namespace Marketplace.Modules.Identity.Application.Roles.Queries.GetRole;

/// <summary>Récupère un rôle et ses permissions.</summary>
public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleSummary>;
