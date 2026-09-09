using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>Retrouve le compte par son adresse, hache le jeton, et délègue à l'agrégat.</summary>
internal sealed class ConfirmEmailCommandHandler : ICommandHandler<ConfirmEmailCommand>
{
    /// <summary>
    /// LA MÊME ERREUR POUR TOUS LES REFUS, ET C'EST LE POINT.
    ///
    /// Adresse inconnue, jeton faux, jeton expiré : trois causes, une seule réponse.
    /// Distinguer « compte introuvable » de « jeton invalide » rétablirait exactement
    /// l'oracle qu'on vient de fermer en amont — il suffirait d'envoyer un jeton
    /// bidon et de lire le message d'erreur pour savoir si l'adresse existe.
    /// </summary>
    private static Error InvalidConfirmation => Error.Validation(
        "identity.email.confirmation_invalid",
        "Lien de confirmation invalide ou expiré. Demandez-en un nouveau.");

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ConfirmEmailCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // LE HACHAGE EST FAIT AVANT LE TEST, PAS APRÈS.
        //
        // Le message d'erreur est le même dans les trois cas de refus ; le TEMPS DE
        // RÉPONSE, lui, ne l'était pas si l'adresse inconnue sortait avant le hachage.
        // C'est le même oracle, mesuré au chronomètre au lieu d'être lu dans le corps.
        // La différence est faible tant que Hash() reste un condensat rapide, mais elle
        // deviendrait exploitable le jour où quelqu'un le remplacerait par une dérivation
        // lente — et ce jour-là, personne ne relirait cette ligne.
        var providedHash = _tokenGenerator.Hash(command.Token);

        if (user is null)
        {
            return Result.Failure(InvalidConfirmation);
        }

        var result = user.ConfirmEmail(providedHash, DateTime.UtcNow);
        if (result.IsFailure)
        {
            // ON SAUVEGARDE MÊME EN ÉCHEC, ET C'EST TOUT L'INTÉRÊT DU COMPTEUR.
            //
            // L'agrégat vient peut-être d'incrémenter le nombre d'essais ratés, et de
            // purger le code au cinquième. Sans cette écriture, le compteur ne survivrait
            // pas à la requête : il repartirait de zéro à chaque tentative, et le plafond
            // ne plafonnerait rien. C'est le seul endroit du parcours où un échec doit
            // laisser une trace en base.
            //
            // La réponse, elle, ne change pas d'un iota : même code, même message, même
            // statut — l'uniformité établie plus haut n'est pas entamée.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // On REMPLACE l'erreur du domaine plutôt que de la propager : elle est
            // précise, et cette précision servirait ici à l'attaquant plus qu'à
            // l'utilisateur, qui n'a de toute façon qu'une action possible — redemander
            // un lien.
            return Result.Failure(InvalidConfirmation);
        }

        // La confirmation de l'e-mail ACTIVE le compte en libre-service (même règle
        // que VerifyEmailCode) : sans cela, un compte confirmé resterait
        // PendingVerification et ne pourrait jamais se connecter. Idempotent —
        // Approve() n'est appelé que sur un compte encore en attente.
        if (user.Status == UserStatus.PendingVerification)
        {
            user.Approve();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
