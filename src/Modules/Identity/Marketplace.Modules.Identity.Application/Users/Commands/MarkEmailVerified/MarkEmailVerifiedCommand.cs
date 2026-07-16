using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.MarkEmailVerified;

/// <summary>
/// Un administrateur atteste que l'adresse e-mail appartient bien au titulaire.
///
/// Ce n'est PAS une vérification : personne n'a cliqué de lien, personne n'a prouvé
/// qu'il relevait cette boîte. C'est une attestation humaine, tracée comme telle
/// (<c>User.EmailVerifiedByAdminOnUtc</c>).
///
/// N'active pas le compte — voir <c>ApproveUserCommand</c>. Les deux gestes sont
/// distincts et le resteront.
/// </summary>
public sealed record MarkEmailVerifiedCommand(Guid UserId) : ICommand;
