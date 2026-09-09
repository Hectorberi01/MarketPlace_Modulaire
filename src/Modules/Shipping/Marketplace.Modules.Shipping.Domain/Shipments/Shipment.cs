using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;

namespace Marketplace.Modules.Shipping.Domain.Shipments;

/// <summary>Données d'une ligne à expédier (SKU + quantité).</summary>
public sealed record ShipmentLineDraft(string Sku, int Quantity);

/// <summary>
/// Expédition d'un colis pour un vendeur d'une commande (place de marché multi-
/// vendeur : une commande = potentiellement plusieurs expéditions, une par
/// vendeur / lieu d'expédition). Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Shipment : AggregateRoot<ShipmentId>
{
    private readonly List<ShipmentItem> _items = new();

    private Shipment()
    {
    }

    private Shipment(ShipmentId id, Guid orderId, Guid sellerId, Guid buyerId, Guid shipFromLocationId)
        : base(id)
    {
        OrderId = orderId;
        SellerId = sellerId;
        BuyerId = buyerId;
        ShipFromLocationId = shipFromLocationId;
        Status = ShipmentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ShipFromLocationId { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }

    /// <summary>Lien de suivi cliquable, dérivé du transporteur du catalogue (null si aucun modèle).</summary>
    public string? TrackingUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>
    /// Qui a emporté le colis. NUL tant que personne ne l'a pris — et sur toutes les
    /// expéditions antérieures à ce champ. Voir <see cref="ShipmentDeliveryMode"/>.
    /// </summary>
    public ShipmentDeliveryMode? DeliveryMode { get; private set; }

    /// <summary>
    /// La course du service de dispatch qui a porté cette expédition, quand le mode est
    /// <c>Courier</c>. C'est ce qui permet, depuis une expédition, de remonter au
    /// coursier, à son horodatage et à sa position — et c'est aussi la clé qui rend
    /// <see cref="MarkDeliveredByCourier"/> idempotente face à un rejeu de l'annonce.
    /// </summary>
    public Guid? DeliveryJobId { get; private set; }

    /// <summary>
    /// La course qui a ÉCHOUÉ sur cette expédition, quand un coursier a renoncé.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA SEULE TRACE, CÔTÉ MARKETPLACE, QU'UN COURSIER A ESSAYÉ ET ABANDONNÉ.
    ///
    /// L'échec ne vivait que dans la base du service de dispatch. Ici, une expédition
    /// abandonnée par un coursier était rigoureusement indiscernable d'une expédition
    /// que le vendeur venait tout juste de préparer : même statut « en préparation »,
    /// même absence de mode de livraison, même silence. L'exploitant qui cherchait
    /// pourquoi une commande payée ne bougeait plus n'avait rien à lire.
    ///
    /// C'est AUSSI la clé d'idempotence de <see cref="MarkCourierDeliveryFailed"/> :
    /// le drain du service coursier rejoue son annonce, et la comparaison de course est
    /// ce qui distingue un rejeu — silencieux — d'un SECOND échec, qui lui doit
    /// reprévenir le vendeur.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid? FailedDeliveryJobId { get; private set; }

    /// <summary>
    /// Le motif tel que la COURSE le porte, déjà normalisé par le service de dispatch
    /// (DeliveryJob.Fail : espaces coupés, vide remplacé par un libellé par défaut).
    /// On ne le reformule pas : c'est ce que le vendeur et le support doivent lire.
    /// </summary>
    public string? CourierFailureReason { get; private set; }

    /// <summary>Quand le coursier a renoncé, daté par le service de dispatch.</summary>
    public DateTime? CourierFailedAtUtc { get; private set; }

    public IReadOnlyCollection<ShipmentItem> Items => _items.AsReadOnly();

    public static Result<Shipment> Create(
        Guid orderId, Guid sellerId, Guid buyerId, Guid shipFromLocationId, IEnumerable<ShipmentLineDraft> lines)
    {
        if (orderId == Guid.Empty || sellerId == Guid.Empty)
        {
            return Error.Validation("shipping.refs_required", "Commande et vendeur sont obligatoires.");
        }

        var lineList = lines?.Where(l => l.Quantity > 0).ToList() ?? new List<ShipmentLineDraft>();
        if (lineList.Count == 0)
        {
            return Error.Validation("shipping.no_lines", "Une expédition doit comporter au moins une ligne.");
        }

        var shipment = new Shipment(ShipmentId.New(), orderId, sellerId, buyerId, shipFromLocationId);
        foreach (var line in lineList)
        {
            shipment._items.Add(new ShipmentItem(Guid.NewGuid(), line.Sku, line.Quantity));
        }

        shipment.Raise(new ShipmentCreatedDomainEvent(shipment.Id.Value, orderId, sellerId));
        return shipment;
    }

    public Result MarkPreparing()
    {
        if (Status != ShipmentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "L'expédition n'est pas en attente de préparation."));
        }

        Status = ShipmentStatus.Preparing;
        Raise(new ShipmentPreparingDomainEvent(Id.Value, OrderId, SellerId));
        return Result.Success();
    }

    /// <summary>
    /// Marque « expédiée ». [trackingUrl] est le lien cliquable pré-calculé depuis le
    /// transporteur du catalogue (facultatif : null si le transporteur n'a pas de
    /// modèle d'URL, ou si le nom a été saisi librement).
    /// </summary>
    public Result MarkShipped(string carrier, string trackingNumber, string? trackingUrl = null)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // « EN PRÉPARATION » EST DÉSORMAIS OBLIGATOIRE. C'ÉTAIT UN RACCOURCI, ET IL
        // COUPAIT LA LIVRAISON.
        //
        // Cette garde acceptait aussi `Pending`. Or `MarkPreparing` est ce qui lève
        // `ShipmentPreparingDomainEvent`, qui publie `OrderReadyForDelivery`, qui crée
        // la course chez le service de dispatch. Un vendeur qui appelait `/ship` sans
        // passer par `/prepare` allait donc de « en attente » à « expédiée » sans
        // qu'AUCUNE COURSE NE SOIT JAMAIS CRÉÉE. Rien ne le journalisait, et le tableau
        // de bord renommait même l'état en « Préparée », ce qui faisait lire la
        // préparation comme une écriture comptable facultative plutôt que comme le seul
        // déclencheur de la livraison.
        //
        // Exiger la préparation n'est pas qu'une mise en ordre : dans le modèle à deux
        // canaux, le transporteur tiers est le REPLI d'une course que personne n'a
        // prise. On ne peut donc pas y arriver sans être passé par la préparation, qui
        // est justement ce qui a proposé la course.
        //
        // CONSÉQUENCE POUR L'APP VENDEUR : le bouton « Expédier » exige maintenant que
        // l'expédition ait été marquée préparée. C'est une rupture de comportement
        // assumée — le raccourci qu'elle supprime est précisément le défaut.
        // ═════════════════════════════════════════════════════════════════════════
        if (Status != ShipmentStatus.Preparing)
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "Seule une expédition préparée peut être confiée à un transporteur."));
        }

        if (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber))
        {
            return Result.Failure(Error.Validation("shipping.tracking_required", "Transporteur et numéro de suivi sont obligatoires."));
        }

        // Le canal se décide ICI : un transporteur nommé, c'est le repli tiers.
        DeliveryMode = ShipmentDeliveryMode.Carrier;
        Status = ShipmentStatus.Shipped;
        Carrier = carrier.Trim();
        TrackingNumber = trackingNumber.Trim();
        TrackingUrl = string.IsNullOrWhiteSpace(trackingUrl) ? null : trackingUrl.Trim();
        ShippedAtUtc = DateTime.UtcNow;
        Raise(new ShipmentShippedDomainEvent(Id.Value, OrderId, Carrier, TrackingNumber));
        return Result.Success();
    }

    /// <summary>
    /// Livraison par TRANSPORTEUR TIERS : le colis était en transit, il est arrivé.
    ///
    /// Réservée au repli. La livraison par coursier de la plateforme passe par
    /// <see cref="MarkDeliveredByCourier"/>, qui part d'un autre état.
    /// </summary>
    public Result MarkDelivered()
    {
        if (Status != ShipmentStatus.Shipped)
        {
            return Result.Failure(Error.Conflict("shipping.invalid_transition", "Seule une expédition en transit peut être livrée."));
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
        Raise(new ShipmentDeliveredDomainEvent(Id.Value, OrderId, SellerId));
        return Result.Success();
    }

    /// <summary>
    /// Livraison par un COURSIER DE LA PLATEFORME.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// C'EST LA TRANSITION QUI MANQUAIT, ET SON ABSENCE NE FAISAIT AUCUN BRUIT.
    ///
    /// Le coursier livrait, il était payé, et l'expédition restait « en préparation » :
    /// `MarkDelivered` exige « en transit », et pour y arriver `MarkShipped` réclame un
    /// transporteur et un numéro de suivi — des données qui n'existent pas pour une moto
    /// en propre. La chaîne s'arrêtait là : pas de `ShipmentDeliveredDomainEvent`, donc
    /// la commande jamais « livrée », donc la séquestre du vendeur jamais libérée.
    ///
    /// Chaque composant se comportait pourtant correctement — le coursier était payé, le
    /// crédit était idempotent, l'expédition refusait une transition invalide. Mais
    /// personne n'appelait cette transition, donc personne ne voyait le refus. Un défaut
    /// qui échoue laisse une lettre morte ; celui-ci laissait une commande d'apparence
    /// normale. C'est ce qui l'a rendu invisible aussi longtemps.
    ///
    /// ON PART DE « EN PRÉPARATION », PAS DE « EN TRANSIT ». Pour un coursier interne,
    /// « en transit » n'a jamais lieu : il ramasse et il dépose dans la même course. Lui
    /// faire traverser un état qui ne correspond à rien aurait demandé d'inventer un
    /// transporteur et un numéro de suivi, c'est-à-dire d'écrire en base une fiction
    /// pour satisfaire une machine à états écrite pour l'autre canal.
    ///
    /// IDEMPOTENTE SUR L'IDENTIFIANT DE COURSE. L'annonce vient d'un service distant
    /// dont la file de sortie réessaie ; un rejeu doit être un succès silencieux. On
    /// compare la course, et pas seulement l'état : une expédition déjà livrée par une
    /// AUTRE course serait une incohérence qu'il vaut mieux voir échouer.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="deliveryJobId">La course du service de dispatch qui a porté le colis.</param>
    public Result MarkDeliveredByCourier(Guid deliveryJobId)
    {
        if (deliveryJobId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "shipping.delivery_job_required",
                "L'identifiant de course est obligatoire pour une livraison par coursier."));
        }

        // Rejeu de la même course : succès, sans rien changer ni rien lever.
        if (Status == ShipmentStatus.Delivered
            && DeliveryMode == ShipmentDeliveryMode.Courier
            && DeliveryJobId == deliveryJobId)
        {
            return Result.Success();
        }

        if (Status != ShipmentStatus.Preparing)
        {
            return Result.Failure(Error.Conflict(
                "shipping.invalid_transition",
                "Seule une expédition en préparation peut être livrée par un coursier."));
        }

        DeliveryMode = ShipmentDeliveryMode.Courier;
        DeliveryJobId = deliveryJobId;
        Status = ShipmentStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;

        // MÊME ÉVÉNEMENT que la livraison par transporteur, délibérément : ce qui suit —
        // commande livrée, séquestre libérée, gains du vendeur rendus retirables — ne
        // dépend pas de qui a porté le colis, et n'a aucune raison d'être dupliqué.
        Raise(new ShipmentDeliveredDomainEvent(Id.Value, OrderId, SellerId));
        return Result.Success();
    }

    /// <summary>
    /// Longueur maximale conservée pour le motif d'échec.
    ///
    /// Le service de dispatch normalise le motif mais NE LE BORNE PAS : rien n'empêche
    /// un coursier de coller trois cents lignes dans le champ « raison ». Sans plafond
    /// ici, l'insertion échouerait sur la longueur de colonne, l'annonce serait rejouée,
    /// abandonnée, et le vendeur ne saurait toujours rien — c'est-à-dire exactement le
    /// défaut qu'on ferme. ON TRONQUE, ON NE REFUSE PAS : le fait à enregistrer est
    /// l'échec, pas la prose.
    /// </summary>
    private const int MaxFailureReasonLength = 300;

    /// <summary>
    /// LE COURSIER A RENONCÉ : la course est close en échec, le colis n'est jamais parti.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// ON ANNOTE L'ÉTAT, ON NE L'AVANCE PAS — ET C'EST TOUT L'INTÉRÊT.
    ///
    /// L'expédition RESTE « en préparation ». Ce n'est pas un oubli : c'est l'état d'où
    /// le vendeur peut repartir. Depuis « en préparation », <see cref="MarkShipped"/>
    /// l'accepte et la confie à un transporteur tiers — le repli en cascade prévu au
    /// CDC §5.7. Toute autre destination la condamnerait :
    ///
    ///  • « Livrée » serait un mensonge, et libérerait la séquestre du vendeur pour un
    ///    colis que l'acheteur n'a jamais reçu.
    ///  • « Annulée » est terminal (<see cref="Cancel"/> refuse d'en sortir) et
    ///    supprimerait le repli transporteur pour une commande DÉJÀ PAYÉE : la vente
    ///    serait perdue par une décision technique.
    ///  • « En attente » ferait redescendre le vendeur d'un cran, et surtout ferait
    ///    reperdre <see cref="MarkPreparing"/> — donc reproposerait la course.
    ///
    /// LE COURSIER N'EST PAS PAYÉ, ET RIEN ICI NE PEUT LE PAYER. Aucune livraison n'a eu
    /// lieu ; l'annonce ne porte d'ailleurs aucun montant. Un éventuel dédommagement de
    /// course tentée est une décision d'exploitation, pas une conséquence mécanique.
    ///
    /// IDEMPOTENTE SUR L'IDENTIFIANT DE COURSE, comme
    /// <see cref="MarkDeliveredByCourier"/>. Le drain du service coursier réessaie
    /// jusqu'à dix fois : rejouer la MÊME course ne doit rien réécrire et ne doit RIEN
    /// LEVER, sans quoi le vendeur recevrait dix fois la même mauvaise nouvelle. Une
    /// AUTRE course, en revanche, est un second échec réel — le repli a été retenté et
    /// a échoué à son tour — et il doit être annoncé.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="deliveryJobId">La course du service de dispatch qui a échoué.</param>
    /// <param name="reason">Motif porté par la course. Tronqué, jamais rejeté.</param>
    /// <param name="failedAtUtc">Instant de l'abandon, daté par le service de dispatch.</param>
    public Result MarkCourierDeliveryFailed(Guid deliveryJobId, string? reason, DateTime failedAtUtc)
    {
        if (deliveryJobId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "shipping.delivery_job_required",
                "L'identifiant de course est obligatoire pour un échec de livraison."));
        }

        // Rejeu de la même course : succès, sans rien changer ni rien lever.
        if (FailedDeliveryJobId == deliveryJobId)
        {
            return Result.Success();
        }

        if (Status != ShipmentStatus.Preparing)
        {
            return Result.Failure(Error.Conflict(
                "shipping.invalid_transition",
                "Seule une expédition en préparation peut voir sa course échouer."));
        }

        // LE MOTIF EST CAPTURÉ DANS UNE LOCALE, PAS RELU SUR LA PROPRIÉTÉ.
        // `CourierFailureReason` est déclarée facultative — elle l'est pour toutes les
        // expéditions qui n'ont jamais connu d'échec — et la relire pour alimenter
        // l'événement passerait un `string?` là où un `string` est attendu. La solution
        // n'est pas de la déclarer obligatoire, ce qui serait faux, mais de garder ici la
        // valeur dont on sait qu'elle n'est pas nulle.
        var normalizedReason = NormalizeFailureReason(reason);

        FailedDeliveryJobId = deliveryJobId;
        CourierFailureReason = normalizedReason;
        CourierFailedAtUtc = failedAtUtc;

        // LE MODE DE LIVRAISON RESTE NUL, DÉLIBÉRÉMENT. Nul veut dire « personne n'a
        // emporté ce colis », et c'est exactement la situation : le coursier a renoncé.
        // L'inscrire en `Courier` affirmerait qu'il est parti par ce canal, et ferait
        // lire l'expédition comme portée alors qu'elle est immobile en boutique.

        Raise(new CourierDeliveryFailedDomainEvent(
            Id.Value, OrderId, SellerId, deliveryJobId, normalizedReason));

        return Result.Success();
    }

    /// <summary>Motif utilisable : jamais vide, jamais plus long que la colonne.</summary>
    private static string NormalizeFailureReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Échec de livraison.";
        }

        return trimmed.Length <= MaxFailureReasonLength
            ? trimmed
            : trimmed[..MaxFailureReasonLength];
    }

    public Result Cancel()
    {
        if (Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict("shipping.not_cancellable", "L'expédition n'est plus annulable."));
        }

        Status = ShipmentStatus.Cancelled;
        return Result.Success();
    }
}
