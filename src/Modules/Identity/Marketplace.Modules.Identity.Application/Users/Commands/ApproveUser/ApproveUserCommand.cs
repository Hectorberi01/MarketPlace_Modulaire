using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ApproveUser;

/// <summary>
/// Validation d'un compte par un administrateur : il passe de « en attente » à
/// « actif » et peut désormais se connecter.
///
/// Le pendant du refus est <c>SuspendUserCommand</c> — réversible, et qui conserve
/// tout. On ne supprime pas un compte refusé : la trace d'une inscription douteuse
/// est précisément ce qu'on veut garder.
/// </summary>
public sealed record ApproveUserCommand(Guid UserId) : ICommand;
