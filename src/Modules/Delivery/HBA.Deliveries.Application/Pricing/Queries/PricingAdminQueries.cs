using HBA.Deliveries.Domain.Pricing;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Pricing.Queries;

/// <summary>Une zone, vue de la console d'exploitation.</summary>
public sealed record DeliveryZoneView(
    Guid Id,
    string Name,
    string City,
    decimal BaseFee,
    decimal ExtraFee,
    double RoadFactor,
    int Priority,
    bool IsActive,
    IReadOnlyList<GeoPointView> Boundary);

public sealed record GeoPointView(double Latitude, double Longitude);

/// <summary>Une grille tarifaire, vue de la console d'exploitation.</summary>
public sealed record PricingRuleView(
    Guid Id,
    string Name,
    int Version,
    decimal BasePrice,
    decimal PricePerKm,
    decimal MinimumPrice,
    double? MaximumDistanceKm,
    string? DeliveryType,
    string? VehicleType,
    decimal UrgencySurcharge,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc,
    bool IsActive,
    int Specificity,
    IReadOnlyList<WeightTierView> WeightTiers);

public sealed record WeightTierView(decimal FromKg, decimal? ToKg, decimal Surcharge);

/// <summary>
/// Zones ACTIVES. Le dépôt ne sait lister que celles-là, et c'est cohérent avec
/// son usage principal — le calcul de devis. Retrouver une zone désactivée passe
/// par son identifiant.
/// </summary>
public sealed record ListDeliveryZonesQuery : IQuery<IReadOnlyList<DeliveryZoneView>>;

/// <summary>
/// Grilles actives À CET INSTANT.
///
/// <c>AtUtc</c> est exposé délibérément : c'est ce qui permet de répondre à
/// « quelle grille s'appliquait le 3 août à 14 h ? » — la première question posée
/// quand un montant est contesté.
/// </summary>
public sealed record ListPricingRulesQuery(DateTime? AtUtc = null) : IQuery<IReadOnlyList<PricingRuleView>>;

internal sealed class PricingAdminQueryHandler
    : IQueryHandler<ListDeliveryZonesQuery, IReadOnlyList<DeliveryZoneView>>,
      IQueryHandler<ListPricingRulesQuery, IReadOnlyList<PricingRuleView>>
{
    private readonly IDeliveryZoneRepository _zones;
    private readonly IPricingRuleRepository _rules;

    public PricingAdminQueryHandler(IDeliveryZoneRepository zones, IPricingRuleRepository rules)
    {
        _zones = zones;
        _rules = rules;
    }

    public async Task<Result<IReadOnlyList<DeliveryZoneView>>> Handle(
        ListDeliveryZonesQuery query, CancellationToken cancellationToken)
    {
        var zones = await _zones.ListActiveAsync(cancellationToken);

        return Result.Success<IReadOnlyList<DeliveryZoneView>>(
            zones
                // Par priorité DÉCROISSANTE : c'est l'ordre dans lequel le moteur
                // les consulte, donc celui qui permet de comprendre pourquoi un
                // point est tombé dans telle zone plutôt que telle autre.
                .OrderByDescending(z => z.Priority)
                .Select(z => new DeliveryZoneView(
                    z.Id.Value, z.Name, z.City, z.BaseFee, z.ExtraFee, z.RoadFactor, z.Priority, z.IsActive,
                    z.Boundary.Vertices.Select(v => new GeoPointView(v.Latitude, v.Longitude)).ToList()))
                .ToList());
    }

    public async Task<Result<IReadOnlyList<PricingRuleView>>> Handle(
        ListPricingRulesQuery query, CancellationToken cancellationToken)
    {
        var rules = await _rules.ListActiveAsync(query.AtUtc ?? DateTime.UtcNow, cancellationToken);

        return Result.Success<IReadOnlyList<PricingRuleView>>(
            rules
                // Même ordre que la sélection du moteur : la plus spécifique
                // d'abord. C'est ce qui rend lisible « laquelle va gagner ».
                .OrderByDescending(r => r.Specificity)
                .ThenByDescending(r => r.ValidFromUtc)
                .Select(r => new PricingRuleView(
                    r.Id.Value, r.Name, r.Version, r.BasePrice, r.PricePerKm, r.MinimumPrice,
                    r.MaximumDistanceKm, r.DeliveryType?.ToString(), r.VehicleType?.ToString(),
                    r.UrgencySurcharge, r.ValidFromUtc, r.ValidUntilUtc, r.IsActive, r.Specificity,
                    r.WeightTiers
                        .OrderBy(t => t.FromKg)
                        .Select(t => new WeightTierView(t.FromKg, t.ToKg, t.Surcharge))
                        .ToList()))
                .ToList());
    }
}
