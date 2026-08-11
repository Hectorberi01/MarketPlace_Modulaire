namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>Identité forte d'une livraison.</summary>
public readonly record struct DeliveryId(Guid Value)
{
    public static DeliveryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un livreur.</summary>
public readonly record struct DriverId(Guid Value)
{
    public static DriverId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// D'OÙ VIENT LA LIVRAISON — ET RIEN DE PLUS.
///
/// C'est le principe directeur du cahier d'architecture : le moteur logistique
/// « ne doit pas connaître la nature commerciale du contenu livré ». Un téléphone
/// HBAExpress, un repas HBA Food et un colis d'un site partenaire suivent le même
/// cycle : on cherche un livreur, il collecte, il roule, il remet.
///
/// La source n'est donc PAS là pour faire varier le comportement. Elle sert à
/// facturer, à mesurer, et à savoir qui prévenir. Le jour où l'on écrit
/// « if (source == HbaFood) » dans une règle de dispatch, c'est que la frontière
/// a cédé — et avec elle la possibilité de vendre Delivery à des tiers.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum DeliverySource
{
    /// <summary>Place de marché HBAExpress.</summary>
    HbaExpress = 0,

    /// <summary>Restauration HBA Food.</summary>
    HbaFood = 1,

    /// <summary>Site marchand tiers, via l'API partenaire.</summary>
    ExternalPartner = 2
}

/// <summary>
/// Niveau de service demandé. Il influence la tarification et la priorité de
/// dispatch, jamais le cycle de vie.
/// </summary>
public enum DeliveryType
{
    /// <summary>Course immédiate.</summary>
    Express = 0,

    /// <summary>Course groupée dans la journée.</summary>
    Standard = 1,

    /// <summary>Course sur créneau convenu.</summary>
    Scheduled = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CYCLE DE VIE D'UNE COURSE.
///
/// Reprend la frise du cahier (§12). Trois remarques sur ce qui n'y est pas dit :
///
/// 1. <see cref="Rejected"/> N'EST PAS UN ÉTAT TERMINAL. Un refus de livreur
///    renvoie la course en <see cref="SearchingDriver"/>. C'est le cas NORMAL,
///    pas l'exception : un livreur refuse pour mille raisons légitimes.
///
/// 2. <see cref="NoDriverAvailable"/> est un état d'ATTENTE, pas un échec. À
///    Cotonou aux heures de pointe, ou dans une commune peu couverte, aucun
///    livreur ne répond pendant plusieurs minutes. La course doit rester
///    reprenable — l'annuler automatiquement ferait perdre une vente.
///
/// 3. Il n'existe pas d'état « payé ». Le paiement appartient à la commande,
///    pas à la course. Delivery livre aussi pour des partenaires qui encaissent
///    eux-mêmes.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Créée, pas encore proposée à un livreur.</summary>
    Pending = 0,

    /// <summary>Le dispatch cherche un livreur.</summary>
    SearchingDriver = 1,

    /// <summary>Proposée à un livreur, en attente de sa réponse.</summary>
    DriverAssigned = 2,

    /// <summary>Le livreur a accepté la mission.</summary>
    DriverAccepted = 3,

    /// <summary>Le livreur est au point de collecte.</summary>
    ArrivedAtPickup = 4,

    /// <summary>Le colis est pris en charge.</summary>
    PickedUp = 5,

    /// <summary>En route vers le destinataire.</summary>
    InTransit = 6,

    /// <summary>Le livreur est arrivé chez le destinataire.</summary>
    ArrivedAtDropoff = 7,

    /// <summary>Remise effectuée et prouvée.</summary>
    Delivered = 8,

    /// <summary>Annulée par le donneur d'ordre ou par l'exploitation.</summary>
    Cancelled = 9,

    /// <summary>
    /// Aucun livreur n'a répondu. État d'ATTENTE : le dispatch peut relancer avec
    /// un rayon élargi, ou un opérateur peut affecter à la main.
    /// </summary>
    NoDriverAvailable = 10
}

/// <summary>
/// Comment la remise a été prouvée. Le choix dépend du scénario, pas du client :
/// un colis de valeur exige un code, un repas se contente d'une photo.
/// </summary>
public enum ProofOfDeliveryKind
{
    /// <summary>Aucune preuve exigée.</summary>
    None = 0,

    /// <summary>Code à quatre chiffres communiqué au destinataire.</summary>
    Pin = 1,

    /// <summary>Photo du colis remis.</summary>
    Photo = 2,

    /// <summary>Signature capturée sur l'écran.</summary>
    Signature = 3
}
