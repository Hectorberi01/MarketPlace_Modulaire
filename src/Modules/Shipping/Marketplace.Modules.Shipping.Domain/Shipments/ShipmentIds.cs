namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Identité forte d'une expédition.</summary>
public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'une expédition (livraison d'un colis vendeur).</summary>
/// <summary>
/// QUI PORTE CETTE EXPÉDITION.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX MODES, ET LE MODÈLE N'EN CONNAISSAIT QU'UN.
///
/// `Shipment` a été écrit pour un TRANSPORTEUR TIERS : `MarkShipped` exige un nom de
/// transporteur et un numéro de suivi, et `MarkDelivered` n'accepte que l'état
/// « en transit ». Or la livraison nominale se fait par coursier en propre, à moto,
/// et une moto n'a ni transporteur ni numéro de suivi. Le service de dispatch
/// annonçait donc une course livrée que l'expédition ne pouvait pas enregistrer :
/// elle restait « en préparation » pour toujours, la commande n'atteignait jamais
/// « livrée », et LA SÉQUESTRE DU VENDEUR N'ÉTAIT JAMAIS LIBÉRÉE.
///
/// L'ORDRE DES DEUX MODES N'EST PAS SYMÉTRIQUE. Le coursier interne est le mode
/// nominal ; le transporteur tiers est le REPLI, atteint quand aucun coursier n'a
/// pris la course (repli en cascade, CDC §5.7). C'est pourquoi les deux chemins
/// partent du même état « en préparation » : la préparation dit que le vendeur a
/// emballé, et c'est seulement ensuite qu'on sait qui emporte le colis.
///
/// NUL = PAS ENCORE DÉCIDÉ, et c'est aussi le cas des expéditions antérieures à ce
/// champ. On ne rétro-remplit pas : affirmer après coup par quel canal un colis est
/// parti serait une reconstitution, pas une donnée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum ShipmentDeliveryMode
{
    /// <summary>Coursier de la plateforme, via le service de dispatch. Mode nominal.</summary>
    Courier = 0,

    /// <summary>Transporteur tiers, avec numéro de suivi. Repli.</summary>
    Carrier = 1
}

public enum ShipmentStatus
{
    Pending = 0,
    Preparing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
