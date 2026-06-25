using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RemoveRole;

/// <summary>Retire un rôle d'un utilisateur.</summary>
public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand;
