using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.UpdateRole;

/// <summary>Met à jour le nom et la description d'un rôle.</summary>
public sealed record UpdateRoleCommand(Guid RoleId, string Name, string? Description = null) : ICommand;
