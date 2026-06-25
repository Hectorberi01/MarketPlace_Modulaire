using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;

/// <summary>Met à jour le prénom, le nom et le numéro de téléphone d'un compte.</summary>
public sealed record UpdateUserProfileCommand(Guid UserId, string FirstName, string LastName, string PhoneNumber) : ICommand;
