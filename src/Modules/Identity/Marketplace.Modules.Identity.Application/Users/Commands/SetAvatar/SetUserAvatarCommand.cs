using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.SetAvatar;

/// <summary>
/// Pose ou retire la photo de profil d'un compte.
///
/// ═══════════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE COMMANDE REND L'ANCIEN IDENTIFIANT DISTANT.
///
/// Remplacer une photo laisse l'ancien fichier sur le stockage média. Il faut donc le
/// supprimer — mais le module Identity n'a aucun accès au stockage : c'est un port du
/// module Catalog, et Identity ne le référence pas. Lui donner cette dépendance pour
/// une suppression de fichier serait payer très cher un détail.
///
/// La commande rend donc l'identifiant du fichier REMPLACÉ, et laisse l'appelant — le
/// BFF, qui a les deux sous la main — s'en occuper. Null si aucune photo n'existait,
/// ou si le fichier était déjà le même.
///
/// L'ORDRE COMPTE : on enregistre la nouvelle photo d'abord, on supprime l'ancienne
/// ensuite. L'inverse ferait qu'un échec d'enregistrement laisserait un compte pointant
/// vers un fichier qui n'existe plus — une image cassée au lieu d'une photo. Dans ce
/// sens-ci, le pire est un fichier orphelin que personne ne voit.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SetUserAvatarCommand(Guid UserId, string? Url, string? ExternalId) : ICommand<string?>;

internal sealed class SetUserAvatarCommandHandler : ICommandHandler<SetUserAvatarCommand, string?>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public SetUserAvatarCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string?>> Handle(SetUserAvatarCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure<string?>(
                Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        // Lu AVANT la mutation : après, la propriété porte le nouveau fichier.
        var precedent = user.AvatarExternalId;

        var resultat = user.SetAvatar(command.Url, command.ExternalId);
        if (resultat.IsFailure)
        {
            return Result.Failure<string?>(resultat.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Le même fichier reposé sur lui-même n'est pas un remplacement : ne pas le
        // rendre, sinon l'appelant supprimerait la photo qu'il vient d'enregistrer.
        // Success<string?> explicite plutôt que la conversion implicite : Result<TValue>
        // porte DEUX opérateurs implicites, un depuis TValue et un depuis Error. Avec
        // TValue = string?, un littéral null pourrait viser les deux.
        return Result.Success<string?>(
            string.Equals(precedent, command.ExternalId, StringComparison.Ordinal) ? null : precedent);
    }
}
