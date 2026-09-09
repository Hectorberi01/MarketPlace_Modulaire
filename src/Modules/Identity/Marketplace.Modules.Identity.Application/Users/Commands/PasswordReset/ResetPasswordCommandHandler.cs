using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

internal sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// LA MÊME ERREUR POUR TOUS LES REFUS.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// C'ÉTAIT L'ORACLE D'ÉNUMÉRATION LE MOINS CHER DE LA PLATEFORME.
    ///
    /// Le cas « adresse inconnue » était bien neutralisé — et un commentaire le disait.
    /// Mais l'échec du domaine était ensuite propagé TEL QUEL, et le domaine, lui,
    /// distingue trois causes. Sans aucun appel préalable à `password/forgot` :
    ///
    ///   adresse inconnue → 400 `identity.user.reset_invalid`
    ///   adresse connue   → 409 `identity.user.no_reset_pending`
    ///
    /// Statut différent ET code différent, en UNE requête, sans envoyer d'e-mail, sans
    /// créer de compte, sans rien modifier. Rien à nettoyer derrière soi, rien qui se
    /// voie dans une boîte mail. Aucune des routes fermées la veille n'était aussi
    /// commode.
    ///
    /// Et ça marchait sur les quatre surfaces, dont `/admin/auth/reset-password` : on
    /// énumérait les adresses DES ADMINISTRATEURS.
    ///
    /// Un troisième état, `identity.user.reset_expired`, révélait en prime qu'un compte
    /// avait demandé une réinitialisation il y a plus d'une heure — de la métadonnée
    /// d'activité récente, offerte à un appelant anonyme.
    ///
    /// LE COMMENTAIRE PRÉCÉDENT NE MENTAIT PAS, il était mal placé : il affirmait la
    /// neutralité de la seule branche qui l'avait, trois lignes au-dessus de la fuite.
    /// C'est le motif qu'il faut retenir — une garde correcte suivie d'un `return result`
    /// qui la défait.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static Error InvalidReset => Error.Validation(
        "identity.user.reset_invalid",
        "Lien de réinitialisation invalide ou expiré. Demandez-en un nouveau.");

    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        // ─────────────────────────────────────────────────────────────────────────
        // LES DEUX HACHAGES SONT FAITS AVANT LE TEST, ET LE SECOND COMPTE VRAIMENT.
        //
        // `_passwordHasher.Hash` est du BCrypt à facteur 12 — de l'ordre de 300 ms de
        // calcul délibéré. Le laisser dans la branche « compte trouvé » rendait le refus
        // sur adresse inconnue un ordre de grandeur plus rapide : le corps uniforme
        // qu'on vient d'écrire serait défait au chronomètre, sans rien lire de la
        // réponse.
        //
        // On paie donc le hachage dans tous les cas. C'est du temps CPU dépensé pour
        // rien sur la branche d'échec, et c'est exactement le but.
        // ─────────────────────────────────────────────────────────────────────────
        var providedHash = _tokenGenerator.Hash(command.Token);
        var newPasswordHash = _passwordHasher.Hash(command.NewPassword);

        if (user is null)
        {
            return Result.Failure(InvalidReset);
        }

        var result = user.ResetPassword(providedHash, newPasswordHash, DateTime.UtcNow);
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

            // ON ÉCRASE L'ERREUR DU DOMAINE. Elle est juste et précise — « aucune
            // réinitialisation en attente », « lien expiré » — et cette précision ne
            // sert ici que celui qui sonde : l'utilisateur légitime n'a de toute façon
            // qu'une action possible, redemander un lien.
            //
            // Le domaine garde ses trois erreurs : elles restent utiles en journal et à
            // tout appelant interne. C'est au bord qu'on les aplatit.
            return Result.Failure(InvalidReset);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
