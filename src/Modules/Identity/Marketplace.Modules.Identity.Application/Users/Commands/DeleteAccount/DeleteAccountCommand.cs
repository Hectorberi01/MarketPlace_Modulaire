using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.DeleteAccount;

/// <summary>
/// L'utilisateur supprime SON PROPRE compte.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LE MOT DE PASSE EST EXIGÉ, ET CE N'EST PAS DE LA PARANOÏA.
///
/// L'action est IRRÉVERSIBLE. Un téléphone déverrouillé posé sur une table, un enfant,
/// un collègue farceur, un conjoint en colère — et le compte disparaît, avec l'historique
/// de commandes, sans aucun retour possible.
///
/// Redemander le mot de passe est le seul moyen de s'assurer que c'est bien le titulaire
/// qui décide. Apple ne l'impose pas ; le bon sens, si.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed record DeleteAccountCommand(Guid UserId, string Password) : ICommand;

internal sealed class DeleteAccountCommandHandler : ICommandHandler<DeleteAccountCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DeleteAccountCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", "Compte introuvable."));
        }

        // Déjà supprimé : on ne se plaint pas, il n'y a plus rien à faire. Renvoyer une
        // erreur ici ferait échouer un second appui sur le bouton — et l'utilisateur
        // croirait que sa suppression n'a pas marché.
        if (user.Status == UserStatus.Deleted)
        {
            return Result.Success();
        }

        if (!_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return Result.Failure(Error.Unauthorized(
                "identity.account.wrong_password",
                "Mot de passe incorrect. La suppression du compte est définitive : nous devons nous assurer que c'est bien vous."));
        }

        var result = user.Anonymize(DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
