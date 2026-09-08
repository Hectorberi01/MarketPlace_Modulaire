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

    /// <summary>
    /// Commandes restées EN ATTENTE DE PAIEMENT au-delà d'une date limite — les paniers
    /// payés à moitié, ceux qu'on ferme et qu'on ne rouvre jamais.
    ///
    /// Elles ne sont pas inertes : chacune retient du stock réservé et, depuis que la
    /// cagnotte se dépense au checkout, de l'argent du client bloqué dans son compte
    /// réservé — ni dépensable, ni retirable, ni visible nulle part. Une commande
    /// abandonnée coûte donc à deux personnes à la fois, et indéfiniment.
    ///
    /// Renvoie les IDENTIFIANTS seuls : l'annulation recharge chaque commande dans sa
    /// propre unité de travail, pour qu'une commande en échec n'emporte pas le lot.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAbandonedIdsAsync(
        DateTime cutoffUtc, int take, CancellationToken cancellationToken = default);

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
