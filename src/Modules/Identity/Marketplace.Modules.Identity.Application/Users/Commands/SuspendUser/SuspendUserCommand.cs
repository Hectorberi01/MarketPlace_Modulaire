using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.SuspendUser;

/// <summary>Suspend un compte (révoque ses sessions).</summary>
public sealed record SuspendUserCommand(Guid UserId) : ICommand;
