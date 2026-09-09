using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;

/// <summary>
/// Hashe le code fourni et le compare au code en attente (comparaison à temps
/// constant, côté domaine). Succès = e-mail marqué vérifié et code purgé.
/// </summary>
internal sealed class VerifyEmailCodeCommandHandler : ICommandHandler<VerifyEmailCodeCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public VerifyEmailCodeCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// LA MÊME ERREUR POUR TOUS LES REFUS, ET C'EST LE POINT.
    ///
    /// Adresse inconnue, code faux, code expiré, code déjà consommé : quatre causes, une
    /// seule réponse. Un « compte introuvable » distinct rouvrirait exactement l'oracle
    /// qu'on vient de fermer en amont — il suffirait d'envoyer un code bidon et de lire
    /// le message pour savoir si l'adresse existe.
    /// </summary>
    private static Error InvalidVerification => Error.Validation(
        "identity.email.verification_invalid",
        "Code de vérification invalide ou expiré. Demandez-en un nouveau.");

    public async Task<Result> Handle(VerifyEmailCodeCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // LE HACHAGE EST FAIT AVANT LE TEST, PAS APRÈS : sortir plus tôt sur une adresse
        // inconnue rendrait le refus mesurable au chronomètre. Même oracle, autre canal.
        var providedHash = _tokenGenerator.Hash(command.Code);

        if (user is null)
        {
            return Result.Failure(InvalidVerification);
        }

        var result = user.ConsumeEmailVerificationCode(providedHash, DateTime.UtcNow);
        if (result.IsFailure)
        {
            // On REMPLACE l'erreur du domaine : sa précision servirait ici l'attaquant
            // plus que l'utilisateur, qui n'a de toute façon qu'une action possible.
            return Result.Failure(InvalidVerification);
        }

        // La confirmation de l'e-mail ACTIVE le compte en libre-service : c'est
        // l'instant où l'on sait que l'adresse est réelle. Tant qu'il n'a pas
        // confirmé, le compte reste PendingVerification et ne peut pas se connecter.
        // Idempotent : Approve() n'est appelé que sur un compte encore en attente.
        if (user.Status == UserStatus.PendingVerification)
        {
            user.Approve();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
