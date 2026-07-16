using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Login;

/// <summary>
/// Authentifie un utilisateur. Si la MFA est active et qu'aucun code n'est
/// fourni, renvoie MfaRequired = true ; l'appelant rappelle avec le code.
/// </summary>
/// <param name="RequiredRoles">
/// Rôles admis sur la surface appelante. <c>null</c> = aucune restriction.
///
/// Chaque BFF déclare ici qui a le droit d'ENTRER chez lui : l'app vendeur passe
/// « Seller », la console d'administration « Admin » et « Moderator », l'app
/// acheteur ne passe rien — elle est ouverte à tous, et doit le rester : un
/// vendeur est aussi quelqu'un qui achète.
///
/// ⚠️ Ce contrôle est du CONFORT et de la défense en profondeur, PAS la barrière.
/// Les quatre BFF partagent la même clé de signature : un jeton pris sur l'app
/// acheteur reste techniquement présentable au BFF vendeur sans repasser par ce
/// login. La vraie barrière, c'est le RequireRole sur les groupes d'endpoints.
/// Celle-ci évite simplement à un acheteur égaré de croire qu'il est connecté à
/// l'app vendeur, pour ne rencontrer que des 403 écran après écran.
/// </param>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? MfaCode = null,
    IReadOnlyCollection<string>? RequiredRoles = null) : ICommand<LoginResponse>;
