using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.CreateRole;

/// <summary>Crée un rôle avec ses permissions.</summary>
public sealed record CreateRoleCommand(
    string Name,
    string? Description = null,
    IReadOnlyList<string>? Permissions = null) : ICommand<Guid>;
