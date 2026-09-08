namespace Marketplace.Modules.Identity.Contracts;

/// <summary>
/// API in-process publique du module Identity. Seul moyen pour un autre module
/// (Sellers, Notifications…) de lire un compte de façon synchrone — jamais en
/// touchant la base ou les entités internes.
/// </summary>
public interface IIdentityModuleApi
{
    Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compte Mobile Money par défaut de l'utilisateur, ou null s'il n'en a aucun.
    ///
    /// Appelé par Settlement au moment de verser une cagnotte client : sans ce point
    /// d'entrée, il faudrait redemander au client un numéro qu'il a déjà enregistré,
    /// et le module se retrouverait à stocker des coordonnées bancaires en double —
    /// donc à en avoir une version périmée le jour où le client change d'opérateur.
    ///
    /// Le moyen marqué « par défaut » l'emporte ; sinon le plus récent. Les cartes
    /// sont ignorées : on n'en conserve que les quatre derniers chiffres.
    /// </summary>
    Task<UserPayoutAccount?> GetDefaultPayoutAccountAsync(Guid userId, CancellationToken cancellationToken = default);
}
