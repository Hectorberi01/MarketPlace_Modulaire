namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Identité forte d'une expédition.</summary>
public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// QUI PORTE CETTE EXPÉDITION.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX MODES ONT ÉCHANGÉ LEURS RÔLES (09/10/2026).
///
/// `Shipment` avait été écrit pour un TRANSPORTEUR TIERS : `MarkShipped` exige un nom
/// de transporteur et un numéro de suivi, `MarkDelivered` n'accepte que l'état « en
/// transit ». Le coursier en propre a ensuite été greffé par-dessus — à moto, sans
/// transporteur ni numéro de suivi — avec ses propres transitions
/// (`MarkDeliveredByCourier`, `MarkCourierDeliveryFailed`) et le dispatch pour les
/// déclencher.
///
/// LA PLATEFORME NE LIVRE PLUS ELLE-MÊME. Le service de dispatch et le réseau de
/// coursiers sont retirés ; un partenaire externe prend le relais. Le transporteur
/// tiers redevient donc ce qu'il était au départ : LE MODE NOMINAL. Le coursier
/// devient l'héritage.
///
/// LA VALEUR `Courier` RESTE, ET CE N'EST PAS UN OUBLI. Ce champ est persisté EN
/// CHAÎNE (`HasConversion&lt;string&gt;`) : les expéditions passées portent littéralement
/// « Courier » en base. Retirer la valeur de cette énumération les rendrait illisibles
/// par EF — un échec de conversion à la lecture, sur des commandes déjà livrées. Elle
/// disparaîtra quand ces lignes auront été archivées, pas avant.
///
/// NUL = PAS ENCORE DÉCIDÉ, et c'est aussi le cas des expéditions antérieures à ce
/// champ. On ne rétro-remplit pas : affirmer après coup par quel canal un colis est
/// parti serait une reconstitution, pas une donnée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum ShipmentDeliveryMode
{
    /// <summary>
    /// Coursier de la plateforme. HÉRITÉ — plus aucune expédition n'est créée dans ce
    /// mode depuis le retrait du dispatch. Conservé pour relire l'existant.
    /// </summary>
    Courier = 0,

    /// <summary>
    /// Transporteur tiers, avec numéro de suivi. Annoncé par le vendeur via
    /// `MarkShipped` une fois le colis remis au transporteur.
    /// </summary>
    Carrier = 1,

    /// <summary>
    /// LE VENDEUR REMET LE COLIS LUI-MÊME. Mode nominal depuis le retrait de la
    /// livraison en propre (09/10/2026).
    ///
    /// Il n'y a alors NI transporteur NI numéro de suivi — une moto n'en a pas. C'est
    /// exactement l'impasse qui avait justifié le mode `Courier` : `MarkShipped` exige
    /// les deux, `MarkDelivered` exige « en transit », et une expédition qui ne peut
    /// franchir ni l'un ni l'autre reste en préparation pour toujours. La commande
    /// n'atteint jamais « livrée », ET LA SÉQUESTRE DU VENDEUR N'EST JAMAIS LIBÉRÉE.
    ///
    /// La remise se prouve donc autrement : par un CODE que l'acheteur donne au
    /// vendeur de la main à la main. Voir `Shipment.MarkDeliveredBySeller`.
    /// </summary>
    Seller = 2
}

/// <summary>Statut d'une expédition (livraison d'un colis vendeur).</summary>
public enum ShipmentStatus
{
    Pending = 0,
    Preparing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
