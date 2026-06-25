using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.DeleteRole;

/// <summary>Supprime un rôle non-système.</summary>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;
