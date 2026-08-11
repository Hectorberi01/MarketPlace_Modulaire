using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>Identité forte d'un devis.</summary>
public readonly record struct DeliveryQuoteId(Guid Value)
{
    public static DeliveryQuoteId New() => new(Guid.NewGuid());

    /// <summary>Référence lisible, à la manière des prestataires de paiement : « qt_… ».</summary>
    public string ToPublicId() => $"qt_{Value:N}";

    /// <summary>Relit une référence publique. Nulle si la forme ne correspond pas.</summary>
    public static DeliveryQuoteId? FromPublicId(string? value)
        => value is not null && value.StartsWith("qt_", StringComparison.Ordinal)
           && Guid.TryParseExact(value[3..], "N", out var parsed)
            ? new DeliveryQuoteId(parsed)
            : null;

    public override string ToString() => ToPublicId();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN PRIX FIGÉ — LA PIÈCE QUI REND LA TARIFICATION HONNÊTE.
///
/// POURQUOI UN DEVIS PERSISTÉ PLUTÔT QU'UN CALCUL À LA VOLÉE
///
/// Sans lui, un client obtient 2 000 FCFA à l'affichage et paie 2 500 au
/// paiement, parce qu'une grille a changé entre-temps ou qu'il a mis six minutes
/// à saisir son adresse. Le devis fige : le montant annoncé est celui facturé,
/// pendant une durée bornée.
///
/// IL FIGE AUSSI SES ENTRÉES, PAS SEULEMENT SON RÉSULTAT
///
/// Points, poids, véhicule, niveau de service, grille et version : tout est
/// conservé. C'est ce qui permet, six mois plus tard et sur un devis contesté, de
/// rejouer exactement le même calcul. Un montant sans ses entrées n'est pas une
/// preuve, c'est une affirmation.
///
/// LA CONSOMMATION EST À USAGE UNIQUE
///
/// Un devis consommé ne peut plus l'être : sans cela, un partenaire créerait dix
/// courses avec un seul devis obtenu au meilleur moment.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryQuote : AggregateRoot<DeliveryQuoteId>
{
    /// <summary>
    /// Durée de validité. Quinze minutes : assez pour saisir une adresse et
    /// confirmer un panier, trop court pour spéculer sur un changement de grille.
    /// </summary>
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(15);

    private DeliveryQuote(
        DeliveryQuoteId id,
        Coordinates pickup,
        Coordinates dropoff,
        Weight weight,
        PackageSize size,
        DeliveryType deliveryType,
        VehicleType vehicleType,
        QuoteBreakdown breakdown,
        Guid? partnerId)
        : base(id)
    {
        Pickup = pickup;
        Dropoff = dropoff;
        Weight = weight;
        Size = size;
        DeliveryType = deliveryType;
        VehicleType = vehicleType;

        DistanceKm = breakdown.DistanceKm;
        EstimatedDurationMinutes = breakdown.EstimatedDurationMinutes;
        BasePrice = breakdown.BasePrice;
        DistancePrice = breakdown.DistancePrice;
        ZoneSurcharge = breakdown.ZoneSurcharge;
        VehicleSurcharge = breakdown.VehicleSurcharge;
        WeightSurcharge = breakdown.WeightSurcharge;
        UrgencySurcharge = breakdown.UrgencySurcharge;
        Total = breakdown.Total;
        Currency = breakdown.Currency;
        PricingRuleId = breakdown.PricingRuleId;
        PricingVersion = breakdown.PricingVersion;
        PickupZoneId = breakdown.PickupZoneId;
        DropoffZoneId = breakdown.DropoffZoneId;

        PartnerId = partnerId;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = CreatedAtUtc + Validity;
    }

    // Requis par EF Core.
    private DeliveryQuote()
    {
        Pickup = null!;
        Dropoff = null!;
        Weight = null!;
        Currency = PricingCalculator.Currency;
    }

    // ── Entrées figées ──────────────────────────────────────────────────────
    public Coordinates Pickup { get; private set; }
    public Coordinates Dropoff { get; private set; }
    public Weight Weight { get; private set; }
    public PackageSize Size { get; private set; }
    public DeliveryType DeliveryType { get; private set; }
    public VehicleType VehicleType { get; private set; }

    // ── Résultat figé ───────────────────────────────────────────────────────
    public double DistanceKm { get; private set; }
    public int EstimatedDurationMinutes { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal DistancePrice { get; private set; }
    public decimal ZoneSurcharge { get; private set; }
    public decimal VehicleSurcharge { get; private set; }
    public decimal WeightSurcharge { get; private set; }
    public decimal UrgencySurcharge { get; private set; }
    public decimal Total { get; private set; }
    public string Currency { get; private set; }

    // ── Traçabilité du calcul ───────────────────────────────────────────────
    public Guid PricingRuleId { get; private set; }
    public int PricingVersion { get; private set; }
    public Guid? PickupZoneId { get; private set; }
    public Guid? DropoffZoneId { get; private set; }

    /// <summary>Partenaire demandeur, si le devis vient de l'API publique.</summary>
    public Guid? PartnerId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Course née de ce devis. Nulle tant qu'il n'a pas été consommé.</summary>
    public Guid? ConsumedByDeliveryId { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedByDeliveryId is not null;

    public bool IsExpired(DateTime atUtc) => atUtc >= ExpiresAtUtc;

    /// <summary>
    /// Fige le devis.
    ///
    /// ⚠️ Le véhicule vient du CALCUL (<c>breakdown.Vehicle</c>) et non de la
    /// demande. Quand l'appelant laisse le choix, <c>request.VehicleType</c> est
    /// nul : c'est le moteur qui a tranché, et c'est sa décision qu'il faut
    /// conserver. Recopier la demande enregistrerait « aucun véhicule » sur un
    /// devis pourtant chiffré — et rendrait le calcul irrejouable, donc le montant
    /// indéfendable.
    /// </summary>
    public static DeliveryQuote FromBreakdown(
        QuoteRequest request, QuoteBreakdown breakdown, Guid? partnerId = null)
        => new(DeliveryQuoteId.New(), request.Pickup, request.Dropoff, request.Weight,
               request.Size, request.DeliveryType, breakdown.Vehicle, breakdown, partnerId);

    /// <summary>
    /// Consomme le devis au profit d'une course.
    ///
    /// Les deux refus distinguent bien l'expiration du double usage : un
    /// partenaire dont le devis a expiré doit en redemander un ; un partenaire
    /// qui rejoue un devis déjà consommé a un défaut dans son intégration, et le
    /// message doit le lui dire.
    /// </summary>
    public Result Consume(Guid deliveryId, DateTime atUtc)
    {
        if (IsConsumed)
        {
            return Result.Failure(
                Error.Conflict("pricing.quote.already_used", "Ce devis a déjà servi à créer une course."));
        }

        if (IsExpired(atUtc))
        {
            return Result.Failure(
                Error.Conflict("pricing.quote.expired",
                    $"Ce devis a expiré le {ExpiresAtUtc:u}. Demandez-en un nouveau."));
        }

        ConsumedByDeliveryId = deliveryId;
        ConsumedAtUtc = atUtc;
        return Result.Success();
    }
}

/// <summary>Accès aux devis.</summary>
public interface IDeliveryQuoteRepository
{
    Task<DeliveryQuote?> GetByIdAsync(DeliveryQuoteId id, CancellationToken cancellationToken = default);

    Task AddAsync(DeliveryQuote quote, CancellationToken cancellationToken = default);
}
