using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

/// <summary>Inscrit un nouvel utilisateur.</summary>
/// <param name="CreatedByAdmin">
/// L'inscription vient-elle de la console d'administration, ou du public ?
///
/// La distinction n'est pas cosmétique : elle décide si le compte naît en attente
/// de validation ou actif. Un compte créé par un administrateur, RCCM en main, n'a
/// pas à être revalidé par ce même administrateur — cela ne prouverait rien et ne
/// ferait que remplir la file de son propre travail. Les deux cas restent pilotés
/// par la configuration (section « Identity:Registration »).
/// </param>
public sealed record RegisterUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password,
    bool CreatedByAdmin = false) : ICommand<Guid>;
