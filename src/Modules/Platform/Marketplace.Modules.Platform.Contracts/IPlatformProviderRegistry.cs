namespace Marketplace.Modules.Platform.Contracts;

/// <summary>
/// Lecture du registre des fournisseurs, pour les modules qui doivent en appeler un.
///
/// C'EST UNE LECTURE DU CHEMIN CHAUD : elle est sollicitée à chaque paiement et à
/// chaque notification. L'implémentation met donc en cache (ICacheService) et
/// invalide sur écriture. Les appelants peuvent l'appeler sans précaution.
///
/// IL N'Y A PAS DE REPLI SILENCIEUX. Aucun fournisseur actif sur un canal renvoie
/// `null`, et l'appelant doit échouer franchement. Retomber sur un adaptateur simulé
/// reproduirait la panne que la garde de PaymentsModuleInstaller a été écrite pour
/// fermer : des encaissements imaginaires, sans erreur ni trace.
/// </summary>
public interface IPlatformProviderRegistry
{
    /// <summary>
    /// Fournisseur actif de plus haute priorité sur ce canal, ou <c>null</c>.
    /// Pour le canal Paiement, plusieurs sont actifs : préférer
    /// <see cref="ListEnabledAsync"/> ou <see cref="FindAsync"/>.
    /// </summary>
    Task<ProviderConfiguration?> GetActiveAsync(PlatformChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Tous les fournisseurs actifs du canal, par priorité croissante.</summary>
    Task<IReadOnlyList<ProviderConfiguration>> ListEnabledAsync(PlatformChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Un fournisseur précis, s'il est ACTIF. Le chemin du paiement : l'acheteur a
    /// choisi « FedaPay », on veut FedaPay ou rien.
    /// </summary>
    Task<ProviderConfiguration?> FindAsync(PlatformChannel channel, string code, CancellationToken cancellationToken = default);
}
