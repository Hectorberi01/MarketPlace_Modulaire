namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Identité forte d'une expédition.</summary>
public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'une expédition (livraison d'un colis vendeur).</summary>
public enum ShipmentStatus
{
    Pending = 0,
    Preparing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// COLIS EMBALLÉ, EN ATTENTE D'ENLÈVEMENT.
    ///
    /// C'est le geste du vendeur qui déclenche la recherche d'un livreur. Il
    /// manquait, et son absence laissait deux mauvais choix : créer la course à
    /// la confirmation de commande — le dispatch propose en 45 secondes, donc le
    /// livreur arrive avant que le vendeur n'ait emballé et attend en boutique —
    /// ou la créer à « expédié », qui présuppose un transporteur externe et
    /// n'aurait plus rien à enlever.
    ///
    /// ⚠️ VALEUR 5 ET NON 2, MALGRÉ SA PLACE DANS LE CYCLE DE VIE.
    ///
    /// L'insérer entre Preparing et Shipped décalerait Shipped, Delivered et
    /// Cancelled d'un cran. Ces entiers sont déjà écrits en base, et rien ne
    /// protesterait : toutes les expéditions livrées deviendraient « annulées »
    /// du jour au lendemain. L'ordre de l'énumération n'est pas l'ordre du
    /// métier — c'est un détail de stockage.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    ReadyForPickup = 5
}
