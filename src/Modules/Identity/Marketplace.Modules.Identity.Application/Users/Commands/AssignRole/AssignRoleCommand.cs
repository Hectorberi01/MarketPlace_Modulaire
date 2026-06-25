using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;

/// <summary>Assigne un rôle à un utilisateur (cumul possible).</summary>
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand;
