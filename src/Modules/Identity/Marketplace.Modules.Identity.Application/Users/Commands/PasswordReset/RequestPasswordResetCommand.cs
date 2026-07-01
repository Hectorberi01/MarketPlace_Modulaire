using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

/// <summary>
/// Demande de réinitialisation du mot de passe. Génère un jeton à usage unique
/// (valide 1h) ; renvoie sa valeur en clair (à transmettre par e-mail/SMS). Pour
/// éviter l'énumération de comptes, renvoie null sans erreur si l'e-mail est inconnu.
/// </summary>
public sealed record RequestPasswordResetCommand(string Email) : ICommand<string?>;
