namespace Marketplace.Modules.Marketing.Domain.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken = default);

    void Remove(Campaign campaign);

    Task<Campaign?> GetByIdAsync(CampaignId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Les campagnes SUSCEPTIBLES d'être publiques : celles qui ont le drapeau et une
    /// adresse.
    ///
    /// C'EST UN SUR-ENSEMBLE, PAS LA RÈGLE. La règle de visibilité vit dans l'agrégat
    /// (`EstVisiblePubliquement`) et tient aussi compte du statut et de la fenêtre. La
    /// réécrire en SQL ici donnerait deux endroits qui décident séparément, et c'est la
    /// vitrine qui trancherait le jour où ils divergent.
    ///
    /// Ce filtre-ci ne retient que des conditions NÉCESSAIRES : il ne peut donc jamais
    /// masquer une campagne que la règle aurait montrée. Il existe parce que la route
    /// qui l'appelle est anonyme, et chargeait jusqu'ici l'historique marketing complet
    /// — brouillons et campagnes terminées comprises — pour en garder deux.
    /// </summary>
    Task<IReadOnlyList<Campaign>> ListPubliablesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Une campagne par son adresse publique. Nulle si aucune ne la porte.
    ///
    /// LA COMPARAISON EST EXACTE, pas insensible à la casse : le slug est normalisé en
    /// minuscules par le domaine au moment où il est posé, donc deux adresses qui ne
    /// diffèrent que par la casse ne peuvent pas coexister. Une comparaison insensible
    /// ici aurait fait croire l'inverse, et empêché l'index unique de servir.
    /// </summary>
    Task<Campaign?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
