namespace Marketplace.Modules.Sellers.Domain.Sellers;

public interface ISellerRepository
{
    Task AddAsync(Seller seller, CancellationToken cancellationToken = default);

    void Remove(Seller seller);

    Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default);

    Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default);

    /// <summary>
    /// TOUS les vendeurs, sans limite. Réservé aux usages qui ont réellement besoin de
    /// l'ensemble. Pour une liste d'écran, voir <see cref="ListPagedAsync"/> : la
    /// console d'administration chargeait la totalité des boutiques à chaque affichage,
    /// invisible à trente vendeurs, bloquant à trois mille.
    /// </summary>
    Task<IReadOnlyList<Seller>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Une page de vendeurs, plus les DÉCOMPTES par statut et par état de dossier KYB.
    ///
    /// Les décomptes ne sont pas un supplément d'âme : sans eux, un écran qui veut
    /// afficher « 12 dossiers à examiner » n'a d'autre choix que de rapatrier toute la
    /// table pour la compter côté client — c'est-à-dire exactement ce que la pagination
    /// vient supprimer. Ils sont calculés sur l'ensemble FILTRÉ PAR LA RECHERCHE mais
    /// AVANT le filtre de statut, pour que les onglets gardent leur compte quand on en
    /// sélectionne un.
    /// </summary>
    Task<(IReadOnlyList<Seller> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts, IReadOnlyDictionary<string, int> KybCounts)> ListPagedAsync(
        int page,
        int pageSize,
        string? search,
        SellerStatus? status,
        KybStatus? kybStatus,
        string? sort,
        bool desc,
        CancellationToken cancellationToken = default);
}
