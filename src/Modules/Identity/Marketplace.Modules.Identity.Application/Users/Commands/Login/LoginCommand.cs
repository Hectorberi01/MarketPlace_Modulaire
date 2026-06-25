using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Login;

/// <summary>
/// Authentifie un utilisateur. Si la MFA est active et qu'aucun code n'est
/// fourni, renvoie MfaRequired = true ; l'appelant rappelle avec le code.
/// </summary>
public sealed record LoginCommand(string Email, string Password, string? MfaCode = null) : ICommand<LoginResponse>;
