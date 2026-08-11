namespace HBA.Deliveries.Domain.Partners;

/// <summary>Accès aux partenaires. L'implémentation vit en Infrastructure.</summary>
public interface IPartnerRepository
{
    Task<Partner?> GetByIdAsync(PartnerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrouve le partenaire portant une clé active dont le préfixe correspond.
    ///
    /// C'est LA requête du chemin d'authentification : elle s'exécute à chaque
    /// appel partenaire, et elle doit toucher un index — d'où la recherche par
    /// préfixe plutôt que par condensat. Le condensat est vérifié ensuite, en
    /// mémoire et à temps constant.
    /// </summary>
    Task<Partner?> FindByApiKeyPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Partner>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Nombre de livraisons créées aujourd'hui par ce partenaire. Alimente le
    /// contrôle de quota.
    /// </summary>
    Task<int> CountDeliveriesTodayAsync(PartnerId id, CancellationToken cancellationToken = default);

    Task AddAsync(Partner partner, CancellationToken cancellationToken = default);
}
