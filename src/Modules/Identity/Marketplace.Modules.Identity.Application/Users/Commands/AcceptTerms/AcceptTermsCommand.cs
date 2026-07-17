using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.AcceptTerms;

/// <summary>
/// L'utilisateur accepte une version donnée des conditions générales.
///
/// La VERSION vient du client, et c'est voulu : c'est exactement le texte qu'il a
/// eu sous les yeux. Enregistrer côté serveur « la version courante » sans savoir
/// laquelle a été affichée reviendrait à faire signer un document qu'on n'a pas
/// montré — et le jour du litige, on ne saurait pas ce qui a été accepté.
/// </summary>
public sealed record AcceptTermsCommand(Guid UserId, string Version) : ICommand;

internal sealed class AcceptTermsCommandHandler : ICommandHandler<AcceptTermsCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AcceptTermsCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AcceptTermsCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var result = user.AcceptTerms(command.Version, DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
