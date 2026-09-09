using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Inventory.Domain.Stock;

/// <summary>
/// Réservation temporaire de stock à la commande, libérée si le paiement échoue
/// (cf. dossier, StockReservation). Entité enfant de l'agrégat InventoryItem.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    private StockReservation()
    {
    }

    internal StockReservation(Guid id, Guid orderId, int quantity, DateTime expiresAtUtc)
        : base(id)
    {
        OrderId = orderId;
        Quantity = quantity;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Cette réservation immobilise-t-elle encore du stock ?
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// `ExpiresAtUtc` ÉTAIT ÉCRITE ET LUE PAR PERSONNE.
    ///
    /// Le champ existait, la colonne existait, la durée était passée à chaque
    /// réservation — et aucune requête, aucun balayeur, aucun calcul ne la consultait.
    /// `Reserved` sommait toutes les lignes sans distinction. Une réservation
    /// n'était donc libérée que par l'annulation de sa commande : si le balayeur des
    /// commandes abandonnées ne tournait pas, elle immobilisait le stock POUR TOUJOURS.
    ///
    /// Une donnée écrite que rien ne lit n'est pas une garantie, c'est une promesse
    /// écrite dans un tiroir.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public bool IsLiveAt(DateTime nowUtc) => ExpiresAtUtc > nowUtc;
}
