namespace Marketplace.Modules.Ordering.Domain.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ acheté pour de bon ? (statut Paid, Confirmed ou Delivered)
    ///
    /// « Pour de bon » exclut Pending et AwaitingPayment à dessein : un panier abandonné au
    /// moment de payer ne doit PAS brûler définitivement la promo de bienvenue de
    /// l'acheteur. Le prix de ce choix est une fenêtre étroite — deux checkouts menés en
    /// parallèle par la même personne pourraient tous deux se croire « premiers » et
    /// obtenir la remise de bienvenue. Le dommage est borné (UNE remise de trop), là où
    /// l'inverse punirait durablement un utilisateur honnête qui a hésité à payer.
    /// </summary>
    Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>Commandes comportant au moins une ligne vendue par ce vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Order>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les commandes de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de commandes pour la console admin : filtre par statut, recherche par
    /// identifiant (commande ou acheteur, GUID exact), tri par date décroissante.
    /// Renvoie le total filtré + la répartition par statut (avant filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);
}
