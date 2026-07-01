using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

/// <summary>Réinitialise le mot de passe à partir du jeton reçu (usage unique, 1h).</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand;
