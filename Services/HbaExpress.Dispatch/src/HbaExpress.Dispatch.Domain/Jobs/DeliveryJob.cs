using HbaExpress.Dispatch.Domain.Couriers;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Domain.Jobs;

/// <summary>Ce qu'il faut pour construire un point de ramassage depuis l'événement plateforme.</summary>
public sealed record PickupDraft(
    Guid ShipmentId, Guid SellerId, string SellerName, string? SellerPhone,
    string CommuneCode, string? Quartier, string? Landmark, string? Line,
    GeoPoint? Position, IReadOnlyList<(string Sku, int Quantity)> Parcels);

/// <summary>Ce qu'il faut pour construire le point de dépôt.</summary>
public sealed record DropoffDraft(
    Guid BuyerId, string? Recipient, string? Phone,
    string CommuneCode, string? Quartier, string? Landmark, string? Line, GeoPoint? Position);

/// <summary>
/// UNE COURSE : ramasser chez un ou plusieurs vendeurs, livrer un acheteur.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INVARIANT QUI COMPTE PLUS QUE TOUS LES AUTRES : UN SEUL GAGNANT.
///
/// L'offre part simultanément à N coursiers. Deux d'entre eux peuvent appuyer sur
/// « J'accepte » dans la même seconde, depuis deux téléphones, sur deux connexions.
/// S'ils gagnent tous les deux, deux hommes roulent vers la même boutique : l'un des
/// deux a perdu son temps et son carburant, la plateforme lui doit une compensation
/// qu'aucune règle ne prévoit, et le vendeur ne sait pas à qui remettre le colis.
///
/// LA GARANTIE TIENT EN DEUX ÉTAGES, ET IL FAUT LES DEUX.
///
///  1. ICI, dans <see cref="Accept"/> : on n'accepte que depuis l'état Offered, et
///     seulement sur une offre encore vivante. Cette règle est explicite, lisible et
///     testable sans base de données.
///
///  2. EN BASE, par jeton de concurrence sur l'agrégat. Le contrôle du dessus lit un
///     état chargé en mémoire : deux transactions parallèles lisent toutes deux
///     « Offered » et passent toutes deux. Seule la base peut arbitrer — la seconde
///     écriture échoue, et l'application répond « course déjà prise ».
///
/// Le premier étage SANS le second donne un code qui a l'air correct et qui ne l'est
/// pas. Le second SANS le premier donne une erreur de concurrence opaque là où le métier
/// avait quelque chose de précis à dire. C'est pourquoi les deux existent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryJob : AggregateRoot<DeliveryJobId>
{
    private readonly List<PickupPoint> _pickups = new();
    private readonly List<DispatchOffer> _offers = new();

    private DeliveryJob()
    {
    }

    private DeliveryJob(DeliveryJobId id, Guid orderId, decimal shippingFee, string currency)
        : base(id)
    {
        OrderId = orderId;
        ShippingFee = shippingFee;
        Currency = currency;
        Status = DeliveryJobStatus.Created;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Commande plateforme. UNE course par commande — c'est l'invariant qui protège des
    /// republications de l'événement « commande prête », que deux vendeurs terminant à la
    /// même seconde peuvent déclencher deux fois.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>Frais de livraison figés au checkout. Ce que l'acheteur a déjà payé.</summary>
    public decimal ShippingFee { get; private set; }

    public string Currency { get; private set; } = default!;
    public DeliveryJobStatus Status { get; private set; }
    public CourierId? AssignedCourierId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Vague d'offres en cours. Incrémentée à chaque ré-offre.</summary>
    public int CurrentRound { get; private set; }

    public Dropoff Dropoff { get; private set; } = default!;

    // ─────────────────────────────────────────────────────────────────────────────
    // VUE BRUTE SUR LE CHAMP, PAS UNE PROJECTION.
    //
    // Ces propriétés renvoyaient une liste TRIÉE, reconstruite à chaque appel. C'est
    // agréable à lire et impossible à mapper : EF a besoin d'une navigation qu'il peut
    // remplir, pas d'un calcul. Le tri est donc offert à part, par PickupsInSequence.
    // ─────────────────────────────────────────────────────────────────────────────
    public IReadOnlyCollection<PickupPoint> Pickups => _pickups.AsReadOnly();

    public IReadOnlyCollection<DispatchOffer> Offers => _offers.AsReadOnly();

    /// <summary>Les ramassages dans l'ordre de collecte.</summary>
    public IReadOnlyList<PickupPoint> PickupsInSequence => _pickups.OrderBy(p => p.Sequence).ToList();

    /// <summary>Le premier ramassage : c'est de LUI qu'on mesure la distance des coursiers.</summary>
    public PickupPoint? FirstPickup => _pickups.OrderBy(p => p.Sequence).FirstOrDefault();

    public static Result<DeliveryJob> Create(
        Guid orderId, decimal shippingFee, string currency,
        IEnumerable<PickupDraft> pickups, DropoffDraft dropoff)
    {
        if (orderId == Guid.Empty)
        {
            return Error.Validation("job.order_required", "La commande est obligatoire.");
        }

        var drafts = pickups?.ToList() ?? new List<PickupDraft>();
        if (drafts.Count == 0)
        {
            return Error.Validation("job.no_pickup", "Une course a besoin d'au moins un point de ramassage.");
        }

        // SANS REPÈRE, LE COURSIER NE TROUVE PAS. On refuse la course plutôt que de
        // l'offrir : un coursier qui accepte puis abandonne faute d'adresse perd son
        // temps, et la course repart de zéro avec un retard qu'on ne rattrape plus.
        if (drafts.Any(d => string.IsNullOrWhiteSpace(d.Landmark)))
        {
            return Error.Validation("job.pickup_landmark_required", "Chaque ramassage doit avoir un point de repère.");
        }

        if (string.IsNullOrWhiteSpace(dropoff.Landmark))
        {
            return Error.Validation("job.dropoff_landmark_required", "Le point de dépôt doit avoir un repère.");
        }

        var job = new DeliveryJob(DeliveryJobId.New(), orderId, shippingFee, currency);

        var sequence = 0;
        foreach (var draft in drafts)
        {
            job._pickups.Add(new PickupPoint(
                Guid.NewGuid(), sequence++, draft.ShipmentId, draft.SellerId, draft.SellerName, draft.SellerPhone,
                draft.CommuneCode, draft.Quartier, draft.Landmark, draft.Line, draft.Position,
                draft.Parcels.Select(p => new Parcel(Guid.NewGuid(), p.Sku, p.Quantity))));
        }

        job.Dropoff = new Dropoff(
            Guid.NewGuid(), dropoff.BuyerId, dropoff.Recipient, dropoff.Phone,
            dropoff.CommuneCode, dropoff.Quartier, dropoff.Landmark, dropoff.Line, dropoff.Position);

        return job;
    }

    /// <summary>
    /// Diffuse la course à une vague de coursiers. Rappelable pour ré-offrir plus large :
    /// les offres encore en attente sont alors SUPPLANTÉES, pas refusées.
    /// </summary>
    public Result Offer(IEnumerable<CourierId> courierIds, DateTime nowUtc, TimeSpan window)
    {
        if (Status is not (DeliveryJobStatus.Created or DeliveryJobStatus.Offered))
        {
            return Result.Failure(Error.Conflict("job.not_offerable", "Cette course n'est plus au stade de la diffusion."));
        }

        var targets = courierIds?.Distinct().ToList() ?? new List<CourierId>();
        if (targets.Count == 0)
        {
            return Result.Failure(Error.Validation("job.no_courier", "Aucun coursier éligible à qui offrir la course."));
        }

        foreach (var offer in _offers)
        {
            offer.Supersede(nowUtc);
        }

        CurrentRound++;
        foreach (var courierId in targets)
        {
            _offers.Add(new DispatchOffer(Guid.NewGuid(), courierId, CurrentRound, nowUtc, nowUtc + window));
        }

        Status = DeliveryJobStatus.Offered;
        return Result.Success();
    }

    /// <summary>
    /// UN coursier prend la course. Voir l'encadré en tête de classe : cette méthode est
    /// le premier des deux étages du verrou, le second étant le jeton de concurrence en
    /// base.
    /// </summary>
    public Result Accept(CourierId courierId, DateTime nowUtc)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // LA PARTICIPATION SE CONTRÔLE AVANT L'ÉTAT. MÊME DÉFAUT QUE
        // `Conversation.SendMessage`, MÊME CORRECTION.
        //
        // Le contrôle de statut passait en premier : un coursier à qui la course n'a
        // JAMAIS été proposée apprenait son cycle de vie — offerte, prise, terminée. Il
        // en résultait quatre réponses distinctes sur cette route, dont trois lisibles
        // par quelqu'un qui n'aurait jamais dû savoir que la course existe.
        //
        // On sait maintenant d'abord si l'appelant a été invité. Tout ce qui suit ne
        // s'adresse qu'à un invité — et à un invité, on peut tout dire.
        // ═════════════════════════════════════════════════════════════════════════
        var known = _offers.Any(o => o.CourierId == courierId);
        if (!known)
        {
            // « INTROUVABLE », et non « pas proposée » : la seconde formulation confirme
            // qu'une course existe à cet identifiant. Le journal, lui, garde la tentative.
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        if (Status != DeliveryJobStatus.Offered)
        {
            // Message volontairement métier : c'est ce que l'application du coursier
            // affichera, et « course déjà prise » se comprend mieux qu'un conflit d'état.
            //
            // CE 403/409 EST CONSERVÉ, LUI. L'appelant a été invité : deux coursiers qui
            // acceptent au même instant est une course normale, pas une attaque, et
            // « déjà prise » est exactement ce qu'il a besoin de lire.
            return Result.Failure(Error.Conflict("job.already_taken", "Cette course a déjà été prise."));
        }

        var offer = _offers.FirstOrDefault(o => o.CourierId == courierId && o.IsLive(nowUtc));
        if (offer is null)
        {
            // Invité, course encore offerte, mais l'offre nominative a expiré : le délai
            // est écoulé pour lui pendant qu'il reste ouvert pour d'autres.
            return Result.Failure(
                Error.Conflict("job.offer_expired", "Le délai pour accepter cette course est écoulé."));
        }

        offer.Accept(nowUtc);
        foreach (var other in _offers.Where(o => o != offer))
        {
            other.Supersede(nowUtc);
        }

        Status = DeliveryJobStatus.Accepted;
        AssignedCourierId = courierId;
        AcceptedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Un coursier refuse explicitement. Son offre meurt, la course reste offerte.</summary>
    public Result Decline(CourierId courierId, DateTime nowUtc)
    {
        var offer = _offers.FirstOrDefault(o => o.CourierId == courierId && o.IsLive(nowUtc));
        if (offer is null)
        {
            // MÊME REFUS QUE `Accept` POUR UN NON-INVITÉ, et pour la même raison :
            // `DeliveryJobService` répond 404 quand la course n'existe pas, donc un code
            // distinct ici confirmerait qu'elle existe. Refuser une course qu'on ne nous
            // a pas proposée n'appelle aucune explication.
            return Result.Failure(Error.NotFound("job.not_found", "Course introuvable."));
        }

        offer.Decline(nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Aucun coursier interne n'a pris la course dans le délai global. La course n'échoue
    /// PAS : elle sort du dispatch interne pour entrer dans le repli en cascade (CDC §5.7).
    /// </summary>
    public Result Expire(DateTime nowUtc)
    {
        if (Status is not (DeliveryJobStatus.Created or DeliveryJobStatus.Offered))
        {
            return Result.Failure(Error.Conflict("job.not_expirable", "Cette course n'est plus en attente d'acceptation."));
        }

        foreach (var offer in _offers)
        {
            offer.Expire(nowUtc);
        }

        Status = DeliveryJobStatus.Expired;
        return Result.Success();
    }

    /// <summary>Le coursier a commencé sa tournée de ramassage.</summary>
    public Result StartPickup()
    {
        if (Status != DeliveryJobStatus.Accepted)
        {
            return Result.Failure(Error.Conflict("job.not_accepted", "La course doit être acceptée avant le ramassage."));
        }

        Status = DeliveryJobStatus.PickingUp;
        return Result.Success();
    }

    public Result MarkPickedUp(Guid pickupPointId, DateTime nowUtc)
    {
        if (Status is not (DeliveryJobStatus.Accepted or DeliveryJobStatus.PickingUp))
        {
            return Result.Failure(Error.Conflict("job.not_picking_up", "La course n'est pas en cours de ramassage."));
        }

        var point = _pickups.FirstOrDefault(p => p.Id == pickupPointId);
        if (point is null)
        {
            return Result.Failure(Error.NotFound("job.pickup_not_found", "Point de ramassage introuvable."));
        }

        point.MarkPickedUp(nowUtc);
        Status = DeliveryJobStatus.PickingUp;
        return Result.Success();
    }

    /// <summary>Un vendeur n'a pas pu remettre le colis : on note et on continue les autres.</summary>
    public Result ReportPickupIncident(Guid pickupPointId, string reason)
    {
        var point = _pickups.FirstOrDefault(p => p.Id == pickupPointId);
        if (point is null)
        {
            return Result.Failure(Error.NotFound("job.pickup_not_found", "Point de ramassage introuvable."));
        }

        point.ReportIncident(string.IsNullOrWhiteSpace(reason) ? "Colis non remis." : reason.Trim());
        return Result.Success();
    }

    /// <summary>
    /// En route vers l'acheteur. EXIGE QUE TOUS LES RAMASSAGES SOIENT FAITS — partir avec
    /// un colis manquant, c'est livrer une commande incomplète et transformer un incident
    /// de ramassage en litige de livraison.
    /// </summary>
    public Result StartTransit()
    {
        if (Status != DeliveryJobStatus.PickingUp)
        {
            return Result.Failure(Error.Conflict("job.not_picking_up", "La course n'est pas en cours de ramassage."));
        }

        if (_pickups.Any(p => !p.IsPickedUp))
        {
            return Result.Failure(Error.Conflict(
                "job.pickups_incomplete", "Tous les ramassages doivent être effectués avant de partir livrer."));
        }

        Status = DeliveryJobStatus.InTransit;
        return Result.Success();
    }

    public Result MarkDelivered(DateTime nowUtc)
    {
        if (Status != DeliveryJobStatus.InTransit)
        {
            return Result.Failure(Error.Conflict("job.not_in_transit", "La course n'est pas en cours de livraison."));
        }

        Status = DeliveryJobStatus.Delivered;
        CompletedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Échec en cours de route (acheteur absent, colis refusé).</summary>
    public Result Fail(string reason, DateTime nowUtc)
    {
        if (Status is DeliveryJobStatus.Delivered or DeliveryJobStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict("job.not_failable", "Cette course est déjà close."));
        }

        Status = DeliveryJobStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Échec de livraison." : reason.Trim();
        CompletedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Annulation (commande annulée côté plateforme, ou décision admin).</summary>
    public Result Cancel(string reason, DateTime nowUtc)
    {
        if (Status is DeliveryJobStatus.Delivered)
        {
            return Result.Failure(Error.Conflict("job.already_delivered", "Une course livrée ne s'annule plus."));
        }

        foreach (var offer in _offers)
        {
            offer.Supersede(nowUtc);
        }

        Status = DeliveryJobStatus.Cancelled;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Course annulée." : reason.Trim();
        CompletedAtUtc = nowUtc;
        return Result.Success();
    }
}
