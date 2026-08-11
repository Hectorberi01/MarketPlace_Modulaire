using HBA.Deliveries.Domain.Deliveries.Events;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE COURSE — LE CŒUR DU MOTEUR LOGISTIQUE.
///
/// CE QUE CET AGRÉGAT IGNORE, ET DOIT CONTINUER D'IGNORER
///
/// Il ne connaît ni produit, ni commande, ni panier, ni vendeur, ni restaurant.
/// Il connaît une RÉFÉRENCE opaque (« ORDER-45821 »), une SOURCE, deux points et
/// un colis. C'est le principe directeur du cahier d'architecture, et c'est aussi
/// ce qui permettra de vendre HBA Delivery à des marchands tiers : le jour où une
/// règle de dispatch demande « est-ce un repas ? », le produit logistique cesse
/// d'être vendable à l'extérieur.
///
/// La tentation viendra. Elle prendra la forme raisonnable de « les repas doivent
/// passer avant les colis ». La bonne réponse est <see cref="DeliveryType"/> :
/// on exprime une URGENCE, pas une nature commerciale.
///
/// POURQUOI UNE MACHINE À ÉTATS EXPLICITE
///
/// Une course a onze états et des retours en arrière légitimes. Sans transitions
/// gardées, on aboutit à des courses livrées sans avoir été collectées, ou
/// réaffectées après remise. Chaque transition vérifie donc d'où elle vient.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Delivery : AggregateRoot<DeliveryId>
{
    /// <summary>
    /// Nombre de propositions avant de rendre la main au dispatch humain.
    ///
    /// Cinq n'est pas un chiffre magique : c'est le point où continuer à proposer
    /// coûte plus cher en attente client qu'en gain de chance. Au-delà, un humain
    /// décide — élargir la zone, majorer la course, ou prévenir le marchand.
    /// </summary>
    public const int MaxDispatchAttempts = 5;

    /// <summary>
    /// Horizon maximal d'une course programmée. Au-delà, ni le vendeur ni le
    /// livreur ne peuvent s'engager utilement, et la course dormirait dans la base
    /// en occupant une place dans toutes les vues d'exploitation.
    /// </summary>
    public const int MaxScheduleHorizonDays = 7;

    private readonly List<DeliveryAssignment> _assignments = new();

    private Delivery(
        DeliveryId id,
        string reference,
        DeliverySource source,
        DeliveryType type,
        DeliveryStop pickup,
        DeliveryStop dropoff,
        DeliveryPackage package,
        ProofOfDeliveryKind requiredProof,
        Guid? partnerId,
        DateTime? scheduledForUtc)
        : base(id)
    {
        Reference = reference;
        Source = source;
        PartnerId = partnerId;
        Type = type;
        Pickup = pickup;
        Dropoff = dropoff;
        Package = package;
        RequiredProof = requiredProof;
        ScheduledForUtc = scheduledForUtc;

        // Le code est émis À LA CRÉATION, pas à la remise : il doit être
        // communiqué au destinataire pendant que la course roule.
        IssuedPin = requiredProof is ProofOfDeliveryKind.Pin ? ProofOfDelivery.IssuePin() : null;

        Status = DeliveryStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private Delivery()
    {
        Reference = string.Empty;
        Pickup = null!;
        Dropoff = null!;
        Package = null!;
    }

    /// <summary>
    /// Référence du donneur d'ordre. OPAQUE pour ce module : on la transporte, on
    /// la renvoie dans les webhooks, on ne l'interprète jamais.
    /// </summary>
    public string Reference { get; private set; }

    public DeliverySource Source { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUEL PARTENAIRE — RENSEIGNÉ SI, ET SEULEMENT SI, LA SOURCE EST EXTERNE.
    ///
    /// Ce champ manquait, et son absence rendait trois choses impossibles :
    /// facturer un partenaire, appliquer son quota, et savoir à quelle URL
    /// envoyer le webhook de fin de course. Une course « ExternalPartner » sans
    /// partenaire est une course que personne ne paie et que personne ne reçoit.
    ///
    /// Il ne contredit PAS le principe directeur du cahier : le moteur ignore
    /// toujours ce qu'il transporte. Il sait seulement à qui rendre des comptes —
    /// ce qui est le propre d'un produit vendu à des tiers.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid? PartnerId { get; private set; }

    /// <summary>Devis dont cette course est issue. Nul pour une course non tarifée.</summary>
    public Guid? QuoteId { get; private set; }

    /// <summary>
    /// Prix convenu, RECOPIÉ depuis le devis plutôt que lu à travers lui.
    ///
    /// La duplication est volontaire : un devis est une pièce éphémère, qu'on
    /// purgera au bout de quelques mois, alors qu'une course doit garder
    /// indéfiniment ce qui a été facturé. Lire le prix « à travers » le devis
    /// reviendrait à perdre la trace du montant le jour où l'on nettoie la table.
    /// </summary>
    public decimal? Price { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// PART DU LIVREUR — FIGÉE À LA REMISE, JAMAIS RECALCULÉE.
    ///
    /// Le taux est une décision commerciale qui bougera : 70 % aujourd'hui, peut-être
    /// 65 % dans six mois. Si l'on ne gardait que le taux courant pour recalculer à la
    /// demande, TOUTES les courses déjà livrées changeraient de montant le jour du
    /// changement — y compris celles déjà payées. Un livreur verrait son historique se
    /// réécrire sous ses yeux.
    ///
    /// On conserve donc les DEUX : le montant, qui fait foi, et le taux appliqué, qui
    /// permet de refaire le calcul six mois plus tard sur un gain contesté. Un montant
    /// sans son taux n'est pas une preuve, c'est une affirmation.
    ///
    /// Nuls tant que la course n'est pas remise : on ne doit rien pour une course
    /// qui n'a pas eu lieu.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal? DriverEarning { get; private set; }

    /// <summary>Taux appliqué au moment de la remise, entre 0 et 1.</summary>
    public decimal? DriverShareRate { get; private set; }

    public DeliveryType Type { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DeliveryStop Pickup { get; private set; }

    public DeliveryStop Dropoff { get; private set; }

    public DeliveryPackage Package { get; private set; }

    public ProofOfDeliveryKind RequiredProof { get; private set; }

    /// <summary>
    /// Livreur actuellement en charge. Nul tant que personne n'a accepté.
    ///
    /// ⚠️ Nommé <c>AssignedDriverId</c> et non <c>DriverId</c> : une propriété de
    /// type <c>DriverId?</c> portant le nom <c>DriverId</c> ne bénéficie PAS de la
    /// règle « Color Color » du compilateur, qui ne s'applique qu'à un type
    /// strictement identique — pas à son <c>Nullable&lt;T&gt;</c>. Le type devenait
    /// alors inutilisable en position de paramètre dans cette classe.
    /// </summary>
    public DriverId? AssignedDriverId { get; private set; }

    public IReadOnlyCollection<DeliveryAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Nombre de propositions déjà faites, refus compris.</summary>
    public int DispatchAttempts => _assignments.Count;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? PickedUpAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Preuve effectivement recueillie à la remise. Nulle tant que la course n'est pas remise.</summary>
    public ProofOfDelivery? Proof { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CODE REMIS AU DESTINATAIRE — ÉMIS ICI, JAMAIS MONTRÉ AU LIVREUR.
    ///
    /// Il est engendré à la création quand la course exige un PIN, et il n'a de
    /// sens que si le livreur ne peut pas le lire : c'est le client qui le lui
    /// dicte à la remise. Toute projection destinée à l'application livreur qui
    /// exposerait ce champ viderait le mécanisme de sa substance — la preuve ne
    /// prouverait plus que le livreur est bien arrivé chez quelqu'un.
    ///
    /// Nul pour tout autre genre de preuve.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? IssuedPin { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TENTATIVES DE PREUVE INFRUCTUEUSES — LE COMPTEUR QUI MANQUAIT.
    ///
    /// J'avais écrit, dans ProofOfDelivery, que « le livreur n'a qu'une poignée de
    /// tentatives avant que le client n'appelle le support ». C'était FAUX : rien
    /// ne comptait. Quatre chiffres, dix mille possibilités, et
    /// POST /deliveries/mine/{id}/delivered s'appelle en boucle. Le limiteur de
    /// débit partitionne par compte livreur — c'est son propre seau, 300 par
    /// minute, soit une demi-heure pour épuiser l'espace et clore une livraison
    /// qu'il n'a jamais faite.
    ///
    /// Cinq tentatives ramènent la probabilité à 0,05 %. C'est aussi largement
    /// assez pour un client qui dicte quatre chiffres au téléphone.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int FailedProofAttempts { get; private set; }

    /// <summary>Au-delà, la preuve par code est verrouillée et un humain doit intervenir.</summary>
    public const int MaxFailedProofAttempts = 5;

    /// <summary>La preuve par code est-elle épuisée ?</summary>
    public bool IsProofLocked => FailedProofAttempts >= MaxFailedProofAttempts;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND LA COURSE DOIT ÊTRE LIVRÉE — POUR LE TYPE Scheduled UNIQUEMENT.
    ///
    /// <c>DeliveryType.Scheduled</c> existait sans qu'aucun champ ne dise QUAND.
    /// Une course « programmée » partait donc au dispatch immédiatement, comme
    /// les autres : le type était purement décoratif, et le client qui choisissait
    /// un créneau voyait arriver son livreur tout de suite.
    ///
    /// L'INVARIANT VA DANS LES DEUX SENS : une course programmée DOIT porter une
    /// date, et une course qui n'est pas programmée ne peut pas en porter. Sans
    /// le second sens, une date posée sur une course express serait ignorée en
    /// silence — et personne ne comprendrait pourquoi elle n'est pas respectée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public DateTime? ScheduledForUtc { get; private set; }

    /// <summary>
    /// Combien de temps AVANT l'heure promise on commence à chercher un livreur.
    ///
    /// Chercher à l'heure exacte garantirait d'être en retard : il faut encore
    /// trouver quelqu'un, qu'il rejoigne la boutique et qu'il roule. Quarante-cinq
    /// minutes couvrent un tour de dispatch complet plus un trajet dans Cotonou.
    /// </summary>
    public static readonly TimeSpan ScheduledDispatchLeadTime = TimeSpan.FromMinutes(45);

    /// <summary>Instant à partir duquel une course programmée peut être dispatchée.</summary>
    public DateTime? DispatchWindowOpensAtUtc =>
        ScheduledForUtc is { } due ? due - ScheduledDispatchLeadTime : null;

    /// <summary>La course est close : plus aucune transition n'est possible.</summary>
    public bool IsTerminal => Status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled;

    /// <summary>Rattache la course à son devis et fige le montant facturé.</summary>
    public void AttachQuote(Guid quoteId, decimal price, string currency)
    {
        QuoteId = quoteId;
        Price = price;
        Currency = currency;
    }

    /// <summary>Durée totale, de la création à la remise.</summary>
    public TimeSpan? TotalDuration => DeliveredAtUtc is null ? null : DeliveredAtUtc - CreatedAtUtc;

    // ─────────────────────────────────────────────────────────────────────────
    // CRÉATION
    // ─────────────────────────────────────────────────────────────────────────

    public static Result<Delivery> Create(
        string? reference,
        DeliverySource source,
        DeliveryType type,
        DeliveryStop pickup,
        DeliveryStop dropoff,
        DeliveryPackage package,
        ProofOfDeliveryKind requiredProof = ProofOfDeliveryKind.None,
        Guid? partnerId = null,
        DateTime? scheduledForUtc = null,
        DateTime? nowUtc = null)
    {
        var trimmedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (trimmedReference is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.reference_required", "Une référence de commande est requise."));
        }

        if (pickup is null || dropoff is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.stops_required", "Le point de collecte et le point de remise sont requis."));
        }

        // ─────────────────────────────────────────────────────────────────────
        // COLLECTE ET REMISE AU MÊME ENDROIT : ON REFUSE.
        //
        // Cela n'arrive pas par malveillance mais par copier-coller, dans une
        // intégration partenaire. Le livreur se déplace pour rien, la course est
        // facturée, et le partenaire découvre l'erreur sur sa facture.
        //
        // La comparaison porte sur le repère ET la commune, pas sur le GPS :
        // les positions sont facultatives, et deux relevés du même lieu diffèrent
        // toujours de quelques mètres.
        // ─────────────────────────────────────────────────────────────────────
        if (string.Equals(pickup.CommuneCode, dropoff.CommuneCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pickup.Landmark, dropoff.Landmark, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.same_stop", "Le point de collecte et le point de remise sont identiques."));
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOURCE ET PARTENAIRE VONT ENSEMBLE — DANS LES DEUX SENS.
        //
        // Une course externe sans partenaire ne peut être ni facturée, ni
        // notifiée. Une course HBAExpress AVEC un partenaire est une confusion
        // d'appelant, et elle facturerait un tiers pour une de nos livraisons.
        // Les deux erreurs sont silencieuses si on ne les refuse pas ici.
        // ─────────────────────────────────────────────────────────────────────
        if (source is DeliverySource.ExternalPartner && partnerId is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.partner_required",
                    "Une course de source externe doit désigner le partenaire qui la demande."));
        }

        if (source is not DeliverySource.ExternalPartner && partnerId is not null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.partner_unexpected",
                    "Seule une course de source externe peut désigner un partenaire."));
        }

        // ─────────────────────────────────────────────────────────────────────
        // TYPE ET DATE VONT ENSEMBLE — DANS LES DEUX SENS.
        //
        // Une course programmée sans date est une course qui part tout de suite,
        // en trahissant le créneau promis. Une date sur une course express est
        // ignorée en silence, et personne ne comprend pourquoi elle n'est pas
        // respectée. Les deux erreurs sont muettes si on ne les refuse pas ici.
        // ─────────────────────────────────────────────────────────────────────
        if (type is DeliveryType.Scheduled && scheduledForUtc is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.schedule_required",
                    "Une course programmée doit indiquer l'heure de livraison souhaitée."));
        }

        if (type is not DeliveryType.Scheduled && scheduledForUtc is not null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.schedule_unexpected",
                    "Seule une course de type « Scheduled » peut porter une heure de livraison."));
        }

        if (scheduledForUtc is { } due)
        {
            var now = nowUtc ?? DateTime.UtcNow;

            // Le créneau doit laisser le temps de trouver un livreur ET de rouler.
            // Programmer « dans dix minutes » n'est pas une programmation, c'est
            // une course express avec une promesse qu'on ne tiendra pas.
            if (due <= now + ScheduledDispatchLeadTime)
            {
                return Result.Failure<Delivery>(
                    Error.Validation("delivery.schedule_too_soon",
                        $"Une course programmée doit être demandée au moins "
                        + $"{ScheduledDispatchLeadTime.TotalMinutes:0} minutes à l'avance."));
            }

            if (due > now.AddDays(MaxScheduleHorizonDays))
            {
                return Result.Failure<Delivery>(
                    Error.Validation("delivery.schedule_too_far",
                        $"Une course ne peut pas être programmée à plus de {MaxScheduleHorizonDays} jours."));
            }
        }

        var delivery = new Delivery(
            DeliveryId.New(), trimmedReference, source, type, pickup, dropoff, package,
            requiredProof, partnerId, scheduledForUtc);

        delivery.Raise(new DeliveryCreatedDomainEvent(
            delivery.Id.Value, delivery.Reference, delivery.Source, delivery.Type));

        return delivery;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DISPATCH
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ouvre la recherche d'un livreur.
    ///
    /// ⚠️ REFUSE TANT QUE LA FENÊTRE D'UNE COURSE PROGRAMMÉE N'EST PAS OUVERTE.
    ///
    /// C'est ce qui donne enfin un sens à <c>DeliveryType.Scheduled</c> : sans
    /// cette garde, la création appelait StartSearching immédiatement et la course
    /// partait au dispatch sur-le-champ, quel que soit le créneau promis.
    /// </summary>
    /// <param name="nowUtc">
    /// L'instant courant. Passé en paramètre parce que l'agrégat n'a pas d'horloge —
    /// c'est ce qui rend la règle testable sans attendre quarante-cinq minutes.
    /// </param>
    public Result StartSearching(DateTime? nowUtc = null)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.NoDriverAvailable))
        {
            return InvalidTransition(nameof(StartSearching));
        }

        if (DispatchWindowOpensAtUtc is { } opensAt && (nowUtc ?? DateTime.UtcNow) < opensAt)
        {
            return Result.Failure(Error.Conflict(
                "delivery.scheduled_not_due",
                $"Cette course est programmée pour le {ScheduledForUtc:u} : la recherche d'un livreur "
                + $"s'ouvrira le {opensAt:u}."));
        }

        Status = DeliveryStatus.SearchingDriver;
        Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        return Result.Success();
    }

    /// <summary>Propose la mission à un livreur.</summary>
    public Result AssignTo(DriverId driverId)
    {
        if (Status is not DeliveryStatus.SearchingDriver)
        {
            return InvalidTransition(nameof(AssignTo));
        }

        // Ne jamais reproposer à quelqu'un qui a déjà refusé : il refusera encore,
        // et chaque tour perdu se paie en attente client.
        if (_assignments.Any(a => a.DriverId == driverId && a.Outcome is AssignmentOutcome.Rejected))
        {
            return Result.Failure(
                Error.Conflict("delivery.driver_already_refused", "Ce livreur a déjà refusé cette course."));
        }

        _assignments.Add(DeliveryAssignment.Offer(driverId, DispatchAttempts + 1));
        Status = DeliveryStatus.DriverAssigned;
        Raise(new DeliveryAssignedDomainEvent(Id.Value, driverId.Value));
        return Result.Success();
    }

    /// <summary>Le livreur accepte.</summary>
    public Result AcceptByDriver(DriverId driverId)
    {
        if (Status is not DeliveryStatus.DriverAssigned)
        {
            return InvalidTransition(nameof(AcceptByDriver));
        }

        var offer = CurrentOffer(driverId);
        if (offer is null)
        {
            return Result.Failure(
                Error.Conflict("delivery.not_offered_to_driver", "Cette course n'est pas proposée à ce livreur."));
        }

        offer.Accept();
        AssignedDriverId = driverId;
        AcceptedAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.DriverAccepted;
        Raise(new DeliveryAcceptedDomainEvent(Id.Value, Reference, Source, driverId.Value));
        return Result.Success();
    }

    /// <summary>
    /// Le livreur refuse, ou ne répond pas à temps.
    ///
    /// La course RETOURNE en recherche — c'est le chemin normal. Elle ne bascule
    /// en <see cref="DeliveryStatus.NoDriverAvailable"/> qu'une fois le plafond de
    /// tentatives atteint, et cet état reste reprenable.
    /// </summary>
    public Result RejectByDriver(DriverId driverId, string? reason = null, bool expired = false)
    {
        if (Status is not DeliveryStatus.DriverAssigned)
        {
            return InvalidTransition(nameof(RejectByDriver));
        }

        var offer = CurrentOffer(driverId);
        if (offer is null)
        {
            return Result.Failure(
                Error.Conflict("delivery.not_offered_to_driver", "Cette course n'est pas proposée à ce livreur."));
        }

        if (expired)
        {
            offer.Expire();
        }
        else
        {
            offer.Reject(reason);
        }

        Raise(new DeliveryRejectedByDriverDomainEvent(Id.Value, driverId.Value, reason));

        if (DispatchAttempts >= MaxDispatchAttempts)
        {
            Status = DeliveryStatus.NoDriverAvailable;
            Raise(new DeliveryNoDriverAvailableDomainEvent(Id.Value, Reference, Source, DispatchAttempts));
        }
        else
        {
            Status = DeliveryStatus.SearchingDriver;
            Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        }

        return Result.Success();
    }

    /// <summary>
    /// L'exploitation retire la mission au livreur en charge : panne, absence
    /// prolongée, signalement. La course repart en recherche.
    /// </summary>
    public Result RevokeAssignment(string? reason)
    {
        if (Status is not (DeliveryStatus.DriverAccepted or DeliveryStatus.ArrivedAtPickup))
        {
            return InvalidTransition(nameof(RevokeAssignment));
        }

        var current = _assignments.LastOrDefault(a => a.Outcome is AssignmentOutcome.Accepted);
        current?.Revoke(reason);

        AssignedDriverId = null;
        AcceptedAtUtc = null;
        Status = DeliveryStatus.SearchingDriver;
        Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        return Result.Success();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EXÉCUTION
    // ─────────────────────────────────────────────────────────────────────────

    public Result MarkArrivedAtPickup()
        => Advance(DeliveryStatus.DriverAccepted, DeliveryStatus.ArrivedAtPickup, nameof(MarkArrivedAtPickup));

    public Result MarkPickedUp()
    {
        // On tolère la collecte SANS passage par « arrivé au point de collecte » :
        // un livreur pressé oublie ce bouton, et refuser bloquerait une course
        // physiquement déjà en cours. L'horodatage manquant se voit dans les
        // statistiques ; le colis, lui, avance.
        if (Status is not (DeliveryStatus.ArrivedAtPickup or DeliveryStatus.DriverAccepted))
        {
            return InvalidTransition(nameof(MarkPickedUp));
        }

        PickedUpAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.PickedUp;
        Raise(new DeliveryPickedUpDomainEvent(Id.Value, Reference, Source, AssignedDriverId!.Value.Value));
        return Result.Success();
    }

    public Result MarkInTransit()
        => Advance(DeliveryStatus.PickedUp, DeliveryStatus.InTransit, nameof(MarkInTransit));

    public Result MarkArrivedAtDropoff()
    {
        if (Status is not (DeliveryStatus.InTransit or DeliveryStatus.PickedUp))
        {
            return InvalidTransition(nameof(MarkArrivedAtDropoff));
        }

        Status = DeliveryStatus.ArrivedAtDropoff;
        return Result.Success();
    }

    /// <summary>
    /// Remise au destinataire.
    ///
    /// La preuve est exigée SI ET SEULEMENT SI elle a été demandée à la création.
    /// L'imposer partout ferait échouer des remises légitimes — un livreur sans
    /// réseau ne peut pas envoyer une photo, et le colis est pourtant remis.
    /// </summary>
    /// <param name="driverShareRate">
    /// Part du prix revenant au livreur, entre 0 et 1. Fournie par la couche
    /// Application, qui la lit dans la configuration : le domaine n'a pas d'accès
    /// aux réglages, et un taux codé en dur ici exigerait un déploiement pour être
    /// changé.
    ///
    /// ⚠️ SANS VALEUR PAR DÉFAUT, DÉLIBÉRÉMENT.
    ///
    /// Il en avait une — zéro — et c'était le même piège que le « _ => false » du
    /// dispatch : un appelant qui l'oubliait payait le livreur ZÉRO, en silence,
    /// sur une course parfaitement facturée. Le rendre obligatoire force chaque
    /// nouveau chemin de remise à décider explicitement de la rémunération, au
    /// lieu d'en hériter une par accident.
    /// </param>
    public Result MarkDelivered(string? proofValue, decimal driverShareRate)
    {
        if (Status is not (DeliveryStatus.ArrivedAtDropoff or DeliveryStatus.InTransit))
        {
            return InvalidTransition(nameof(MarkDelivered));
        }

        // ─────────────────────────────────────────────────────────────────────
        // LA PREUVE EST MAINTENANT VÉRIFIÉE, PAS SEULEMENT PRÉSENTE.
        //
        // Auparavant, toute chaîne non vide satisfaisait n'importe quel genre de
        // preuve : un livreur tapait « ok » et une course exigeant une photo se
        // fermait. Le code émis n'était comparé à rien, et aucune image n'était
        // jamais conservée. Voir ProofOfDelivery.
        // ─────────────────────────────────────────────────────────────────────
        ProofOfDelivery? captured = null;

        if (RequiredProof is not ProofOfDeliveryKind.None)
        {
            if (IsProofLocked)
            {
                return Result.Failure(Error.Conflict(
                    "delivery.proof.locked",
                    $"La preuve de cette course est verrouillée après {MaxFailedProofAttempts} tentatives "
                    + "infructueuses. Contactez le support pour la débloquer."));
            }

            var proof = ProofOfDelivery.Capture(RequiredProof, proofValue, IssuedPin, DateTime.UtcNow);
            if (proof.IsFailure)
            {
                // ─────────────────────────────────────────────────────────────
                // ⚠️ ON NE COMPTE QUE LES MAUVAISES RÉPONSES, PAS LES ABSENCES.
                //
                // Une preuve simplement ABSENTE n'est pas une tentative : c'est
                // une application qui n'a pas encore ouvert son écran de saisie.
                // La compter permettrait de verrouiller la course d'un livreur en
                // lui faisant appeler cinq fois une route sans corps — un déni de
                // service à un appel près.
                // ─────────────────────────────────────────────────────────────
                if (proof.Error.Code is "delivery.proof.pin_mismatch")
                {
                    FailedProofAttempts++;
                }

                return Result.Failure(proof.Error);
            }

            captured = proof.Value;
        }

        if (driverShareRate is < 0m or > 1m)
        {
            return Result.Failure(
                Error.Validation("delivery.share_rate_invalid",
                    "La part du livreur doit être comprise entre 0 et 1."));
        }

        Proof = captured;
        DeliveredAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.Delivered;

        // ─────────────────────────────────────────────────────────────────────
        // PAS DE PRIX, PAS DE GAIN — ET C'EST UN SIGNAL, PAS UN DÉTAIL.
        //
        // Une course sans montant est une course que personne ne facture et pour
        // laquelle personne ne peut être payé. Mettre zéro serait exact
        // arithmétiquement et faux dans les faits : le livreur a bien roulé.
        //
        // On laisse donc NUL plutôt que zéro. La distinction compte au moment de
        // solder : « aucun gain calculé » se cherche, « zéro franc » se paie.
        //
        // L'arrondi va au franc entier : le XOF n'a pas de subdivision, et un
        // montant à décimales ne peut de toute façon pas être versé.
        // ─────────────────────────────────────────────────────────────────────
        if (Price is { } price)
        {
            DriverShareRate = driverShareRate;
            DriverEarning = Math.Round(price * driverShareRate, 0, MidpointRounding.AwayFromZero);
        }

        Raise(new DeliveryCompletedDomainEvent(
            Id.Value, Reference, Source, AssignedDriverId!.Value.Value, DeliveredAtUtc.Value,
            DriverEarning, Currency));

        return Result.Success();
    }

    /// <summary>
    /// Annulation. Impossible une fois le colis collecté : à ce stade, la
    /// marchandise est chez le livreur et il faut un RETOUR, qui est une autre
    /// course — pas l'effacement de celle-ci.
    /// </summary>
    public Result Cancel(string? reason)
    {
        if (IsTerminal)
        {
            return Result.Failure(
                Error.Conflict("delivery.already_closed", "Cette course est déjà close."));
        }

        if (Status is DeliveryStatus.PickedUp or DeliveryStatus.InTransit or DeliveryStatus.ArrivedAtDropoff)
        {
            return Result.Failure(
                Error.Conflict("delivery.already_picked_up",
                    "Le colis est déjà pris en charge : créez une course de retour plutôt que d'annuler."));
        }

        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = DeliveryStatus.Cancelled;
        Raise(new DeliveryCancelledDomainEvent(Id.Value, Reference, Source, CancellationReason));
        return Result.Success();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private DeliveryAssignment? CurrentOffer(DriverId driverId)
        => _assignments.LastOrDefault(a => a.DriverId == driverId && a.Outcome is AssignmentOutcome.Offered);

    private Result Advance(DeliveryStatus from, DeliveryStatus to, string operation)
    {
        if (Status != from)
        {
            return InvalidTransition(operation);
        }

        Status = to;
        return Result.Success();
    }

    private Result InvalidTransition(string operation)
        => Result.Failure(Error.Conflict(
            "delivery.invalid_transition",
            $"Opération « {operation} » impossible depuis l'état « {Status} »."));
}
