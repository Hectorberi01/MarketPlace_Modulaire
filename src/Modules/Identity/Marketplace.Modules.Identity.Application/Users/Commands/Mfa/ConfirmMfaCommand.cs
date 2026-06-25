using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Mfa;

/// <summary>Confirme l'activation MFA en vérifiant un premier code TOTP.</summary>
public sealed record ConfirmMfaCommand(Guid UserId, string Code) : ICommand;
