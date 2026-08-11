using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Pricing.Queries;

/// <summary>
/// Réponse d'un devis, dans la forme attendue par le cahier — et par les
/// intégrateurs partenaires.
///
/// Les composants somment exactement au total : l'arrondi se fait ligne par
/// ligne. Un partenaire peut donc refaire le calcul et retrouver notre chiffre,
/// ce qui rend le devis vérifiable plutôt que discutable.
/// </summary>
public sealed record QuoteResponse(
    string QuoteId,
    double DistanceKm,
    int EstimatedDurationMinutes,
    decimal BasePrice,
    decimal DistancePrice,
    decimal ZoneSurcharge,
    decimal VehicleSurcharge,
    decimal WeightSurcharge,
    decimal UrgencySurcharge,
    decimal Total,
    string Currency,
    DateTime ExpiresAtUtc,
    int PricingVersion,
    string? PickupZone,
    string? DropoffZone,

    // Le véhicule retenu. Renvoyé TOUJOURS, y compris quand l'appelant l'avait
    // imposé : une réponse dont la forme change selon la demande oblige chaque
    // intégrateur à écrire deux chemins de lecture.
    VehicleType Vehicle);

/// <summary>
/// Demande un devis. <c>PartnerId</c> n'est renseigné que par l'API publique.
///
/// ⚠️ C'est une QUERY qui ÉCRIT — le devis est persisté. L'entorse est assumée :
/// c'est une lecture du point de vue de l'appelant, et l'appeler « commande »
/// obligerait chaque intégrateur à comprendre pourquoi demander un prix modifie
/// quelque chose.
/// </summary>
public sealed record GetQuoteQuery(
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,
    decimal WeightKg,
    PackageSize Size,
    DeliveryType DeliveryType,

    // Facultatif, et l'omettre est le chemin recommandé : le moteur retient alors
    // le plus petit véhicule capable de porter le colis — qui est aussi le moins
    // cher. Un acheteur n'a pas à connaître la charge utile d'un tricycle.
    VehicleType? VehicleType = null,
    Guid? PartnerId = null) : IQuery<QuoteResponse>;

internal sealed class GetQuoteQueryHandler : IQueryHandler<GetQuoteQuery, QuoteResponse>
{
    private readonly IDeliveryZoneRepository _zones;
    private readonly IPricingRuleRepository _rules;
    private readonly IDeliveryQuoteRepository _quotes;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public GetQuoteQueryHandler(
        IDeliveryZoneRepository zones,
        IPricingRuleRepository rules,
        IDeliveryQuoteRepository quotes,
        IDeliveryUnitOfWork unitOfWork)
    {
        _zones = zones;
        _rules = rules;
        _quotes = quotes;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<QuoteResponse>> Handle(GetQuoteQuery query, CancellationToken cancellationToken)
    {
        var pickup = Coordinates.Create(query.PickupLatitude, query.PickupLongitude);
        if (pickup.IsFailure)
        {
            return Result.Failure<QuoteResponse>(pickup.Error);
        }

        var dropoff = Coordinates.Create(query.DropoffLatitude, query.DropoffLongitude);
        if (dropoff.IsFailure)
        {
            return Result.Failure<QuoteResponse>(dropoff.Error);
        }

        var weight = Weight.FromKilograms(query.WeightKg);
        if (weight.IsFailure)
        {
            return Result.Failure<QuoteResponse>(weight.Error);
        }

        var request = new QuoteRequest(
            pickup.Value, dropoff.Value, weight.Value, query.Size, query.DeliveryType, query.VehicleType);

        // Zones et grilles sont chargées ENTIÈREMENT puis filtrées en mémoire :
        // elles se comptent en dizaines, et le calcul doit rester une fonction
        // pure pour être rejouable.
        var now = DateTime.UtcNow;
        var zones = await _zones.ListActiveAsync(cancellationToken);
        var rules = await _rules.ListActiveAsync(now, cancellationToken);

        var breakdown = PricingCalculator.Calculate(request, zones, rules, now);
        if (breakdown.IsFailure)
        {
            return Result.Failure<QuoteResponse>(breakdown.Error);
        }

        var quote = DeliveryQuote.FromBreakdown(request, breakdown.Value, query.PartnerId);

        await _quotes.AddAsync(quote, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Les noms de zones sont résolus depuis la liste déjà chargée : aucune
        // lecture supplémentaire pour un champ d'affichage.
        string? ZoneName(Guid? id) => id is null ? null : zones.FirstOrDefault(z => z.Id.Value == id)?.Name;

        var b = breakdown.Value;

        return new QuoteResponse(
            quote.Id.ToPublicId(),
            b.DistanceKm,
            b.EstimatedDurationMinutes,
            b.BasePrice,
            b.DistancePrice,
            b.ZoneSurcharge,
            b.VehicleSurcharge,
            b.WeightSurcharge,
            b.UrgencySurcharge,
            b.Total,
            b.Currency,
            quote.ExpiresAtUtc,
            b.PricingVersion,
            ZoneName(b.PickupZoneId),
            ZoneName(b.DropoffZoneId),
            b.Vehicle);
    }
}
