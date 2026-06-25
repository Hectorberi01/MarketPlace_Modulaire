using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

/// <summary>Inscrit un nouvel utilisateur (compte en attente de vérification e-mail).</summary>
public sealed record RegisterUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password) : ICommand<Guid>;
