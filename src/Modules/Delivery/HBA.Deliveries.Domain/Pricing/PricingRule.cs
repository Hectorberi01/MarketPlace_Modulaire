using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>Identité forte d'une règle tarifaire.</summary>
public readonly record struct PricingRuleId(Guid Value)
{
    public static PricingRuleId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Palier de surcharge au poids. Borne basse INCLUSE, borne haute EXCLUE — pour
/// qu'un colis de 5,00 kg tombe dans un seul palier et pas deux.
/// </summary>
public sealed class WeightTier : Entity<Guid>
{
    private WeightTier(Guid id, decimal fromKg, decimal? toKg, decimal surcharge)
        : base(id)
    {
        FromKg = fromKg;
        ToKg = toKg;
        Surcharge = surcharge;
    }

    // Requis par EF Core.
    private WeightTier()
    {
    }

    public decimal FromKg { get; private set; }

    /// <summary>Borne haute exclue. Nulle pour le dernier palier.</summary>
    public decimal? ToKg { get; private set; }

    public decimal Surcharge { get; private set; }

    internal static WeightTier Create(decimal fromKg, decimal? toKg, decimal surcharge)
        => new(Guid.NewGuid(), fromKg, toKg, surcharge);

    public bool Covers(decimal kilograms)
        => kilograms >= FromKg && (ToKg is null || kilograms < ToKg);
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE GRILLE TARIFAIRE.
///
/// UNE SEULE ENTITÉ PLUTÔT QUE SIX TABLES
///
/// Le cahier proposait <c>PricingRules</c>, <c>VehiclePricingRules</c>,
/// <c>WeightPricingRules</c>, <c>DistancePricingRules</c> et
/// <c>DeliveryTypePricingRules</c>. C'est une table par dimension — et chaque
/// devis exigerait alors cinq lectures, cinq jointures, et surtout cinq façons
/// pour une grille d'être incohérente avec elle-même.
///
/// Ici, une grille est une UNITÉ : elle vaut pour un couple (type de livraison,
/// véhicule), porte son prix de base, son prix au kilomètre, son plancher, et
/// ses paliers de poids. On la lit d'un bloc, on la publie d'un bloc, et on
/// l'archive d'un bloc.
///
/// LA SÉLECTION VA DU PLUS PRÉCIS AU PLUS GÉNÉRAL
///
/// Une grille peut ne viser aucun véhicule (<c>null</c> = tous) et aucun type de
/// livraison. La recherche retient donc la plus SPÉCIFIQUE applicable, ce qui
/// permet d'avoir une grille générale et de la surcharger pour la camionnette
/// sans dupliquer le reste.
///
/// LA VERSION EST FIGÉE DANS LE DEVIS
///
/// Un devis émis sous la version 3 reste honoré même si la version 4 est publiée
/// la seconde d'après. Sans ce numéro, un changement de tarif rendrait
/// invérifiable tout devis en cours — et donc contestable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PricingRule : AggregateRoot<PricingRuleId>
{
    private readonly List<WeightTier> _weightTiers = new();

    private PricingRule(
        PricingRuleId id,
        string name,
        int version,
        decimal basePrice,
        decimal pricePerKm,
        decimal minimumPrice,
        double? maximumDistanceKm,
        DeliveryType? deliveryType,
        VehicleType? vehicleType,
        decimal urgencySurcharge,
        DateTime validFromUtc,
        DateTime? validUntilUtc)
        : base(id)
    {
        Name = name;
        Version = version;
        BasePrice = basePrice;
        PricePerKm = pricePerKm;
        MinimumPrice = minimumPrice;
        MaximumDistanceKm = maximumDistanceKm;
        DeliveryType = deliveryType;
        VehicleType = vehicleType;
        UrgencySurcharge = urgencySurcharge;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        IsActive = true;
    }

    // Requis par EF Core.
    private PricingRule() => Name = string.Empty;

    public string Name { get; private set; }

    /// <summary>Numéro figé dans chaque devis émis sous cette grille.</summary>
    public int Version { get; private set; }

    public decimal BasePrice { get; private set; }

    public decimal PricePerKm { get; private set; }

    /// <summary>Plancher : une course très courte coûte quand même le déplacement du livreur.</summary>
    public decimal MinimumPrice { get; private set; }

    /// <summary>Au-delà, cette grille ne s'applique plus. Nul = sans limite.</summary>
    public double? MaximumDistanceKm { get; private set; }

    /// <summary>Nul = vaut pour tous les niveaux de service.</summary>
    public DeliveryType? DeliveryType { get; private set; }

    /// <summary>Nul = vaut pour tous les véhicules.</summary>
    public VehicleType? VehicleType { get; private set; }

    /// <summary>Supplément d'urgence, appliqué aux courses express.</summary>
    public decimal UrgencySurcharge { get; private set; }

    public DateTime ValidFromUtc { get; private set; }

    public DateTime? ValidUntilUtc { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<WeightTier> WeightTiers => _weightTiers.AsReadOnly();

    /// <summary>
    /// Spécificité de la grille : plus le nombre est élevé, plus elle est ciblée.
    /// Sert à départager deux grilles applicables.
    /// </summary>
    public int Specificity => (DeliveryType is null ? 0 : 1) + (VehicleType is null ? 0 : 1);

    public static Result<PricingRule> Create(
        string? name,
        decimal basePrice,
        decimal pricePerKm,
        decimal minimumPrice = 0m,
        double? maximumDistanceKm = null,
        DeliveryType? deliveryType = null,
        VehicleType? vehicleType = null,
        decimal urgencySurcharge = 0m,
        DateTime? validFromUtc = null,
        DateTime? validUntilUtc = null,
        int version = 1)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmed is null)
        {
            return Result.Failure<PricingRule>(
                Error.Validation("pricing.rule.name_required", "Le nom de la grille est requis."));
        }

        if (basePrice < 0 || pricePerKm < 0 || minimumPrice < 0 || urgencySurcharge < 0)
        {
            return Result.Failure<PricingRule>(
                Error.Validation("pricing.rule.negative_amount", "Aucun montant d'une grille ne peut être négatif."));
        }

        var from = validFromUtc ?? DateTime.UtcNow;
        if (validUntilUtc is { } until && until <= from)
        {
            return Result.Failure<PricingRule>(
                Error.Validation("pricing.rule.invalid_period", "La fin de validité doit suivre le début."));
        }

        return new PricingRule(
            PricingRuleId.New(), trimmed, version, basePrice, pricePerKm, minimumPrice,
            maximumDistanceKm, deliveryType, vehicleType, urgencySurcharge, from, validUntilUtc);
    }

    /// <summary>
    /// Ajoute un palier de poids. Les chevauchements sont refusés : deux paliers
    /// qui se recouvrent rendraient la surcharge dépendante de l'ordre de
    /// lecture, donc non reproductible — et un devis non reproductible n'est pas
    /// opposable.
    /// </summary>
    public Result AddWeightTier(decimal fromKg, decimal? toKg, decimal surcharge)
    {
        if (fromKg < 0 || surcharge < 0)
        {
            return Result.Failure(Error.Validation("pricing.tier.negative", "Palier ou surcharge négatif."));
        }

        if (toKg is { } to && to <= fromKg)
        {
            return Result.Failure(Error.Validation("pricing.tier.invalid_range", "La borne haute doit dépasser la borne basse."));
        }

        var overlaps = _weightTiers.Any(t =>
            fromKg < (t.ToKg ?? decimal.MaxValue) && (toKg ?? decimal.MaxValue) > t.FromKg);

        if (overlaps)
        {
            return Result.Failure(
                Error.Conflict("pricing.tier.overlap", "Ce palier de poids en chevauche un autre."));
        }

        _weightTiers.Add(WeightTier.Create(fromKg, toKg, surcharge));
        return Result.Success();
    }

    /// <summary>Surcharge due pour ce poids. Zéro si aucun palier ne le couvre.</summary>
    public decimal WeightSurchargeFor(Weight weight)
        => _weightTiers.FirstOrDefault(t => t.Covers(weight.Kilograms))?.Surcharge ?? 0m;

    /// <summary>La grille s'applique-t-elle à cette demande, à cet instant ?</summary>
    public bool AppliesTo(DeliveryType deliveryType, VehicleType vehicleType, Distance distance, DateTime atUtc)
    {
        if (!IsActive || atUtc < ValidFromUtc || (ValidUntilUtc is { } until && atUtc >= until))
        {
            return false;
        }

        if (DeliveryType is { } dt && dt != deliveryType)
        {
            return false;
        }

        if (VehicleType is { } vt && vt != vehicleType)
        {
            return false;
        }

        return MaximumDistanceKm is not { } max || distance.Kilometers <= max;
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>Accès aux grilles tarifaires.</summary>
public interface IPricingRuleRepository
{
    /// <summary>
    /// Grilles actives à cet instant. Comme les zones : peu nombreuses, lues à
    /// chaque devis, filtrées en mémoire.
    /// </summary>
    Task<IReadOnlyList<PricingRule>> ListActiveAsync(DateTime atUtc, CancellationToken cancellationToken = default);

    Task<PricingRule?> GetByIdAsync(PricingRuleId id, CancellationToken cancellationToken = default);

    Task AddAsync(PricingRule rule, CancellationToken cancellationToken = default);
}
