namespace Marketplace.Modules.Inventory.Domain.Locations;

public interface IFulfillmentLocationRepository
{
    Task AddAsync(FulfillmentLocation location, CancellationToken cancellationToken = default);

    void Remove(FulfillmentLocation location);

    Task<FulfillmentLocation?> GetByIdAsync(FulfillmentLocationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FulfillmentLocation>> ListByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une page des lieux d'expédition de la plateforme (back-office admin), ET LEUR
    /// NOMBRE TOTAL.
    ///
    /// Le total n'est pas une commodité d'affichage. La console s'en sert pour rattacher
    /// un article en rupture à sa boutique, via la chaîne article → emplacement →
    /// vendeur. Quand un emplacement tombe au-delà de la borne, cette chaîne se rompt et
    /// l'écran affiche « emplacement introuvable » : l'administrateur lit « donnée
    /// incohérente » là où la cause est « la liste a été coupée », et une rupture de
    /// stock chez une boutique ancienne devient inexploitable. Avec le total, l'écran
    /// dit lequel des deux c'est.
    /// </summary>
    Task<(IReadOnlyList<FulfillmentLocation> Items, int Total)> ListAllAsync(
        int take = 500, CancellationToken cancellationToken = default);
}
