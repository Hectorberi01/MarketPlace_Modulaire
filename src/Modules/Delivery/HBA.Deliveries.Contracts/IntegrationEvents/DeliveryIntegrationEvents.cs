using Marketplace.BuildingBlocks.IntegrationEvents;

namespace HBA.Deliveries.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES FAITS QUE DELIVERIES PUBLIE AU RESTE DU SYSTÈME.
///
/// Ils portent la RÉFÉRENCE et la SOURCE, jamais un identifiant de commande typé.
/// C'est ce qui permet au même événement de servir trois consommateurs très
/// différents :
///
///   • Ordering, qui reconnaît sa propre référence et fait avancer la commande ;
///   • Wallet, qui crédite le livreur ;
///   • le service de webhooks, qui prévient un partenaire externe dont nous ne
///     savons rien d'autre que cette référence.
///
/// Un événement qui exposerait un « OrderId » forcerait Delivery à connaître
/// Ordering — exactement la dépendance que le moteur logistique doit refuser
/// pour rester vendable à des tiers.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DeliveryCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required string Type { get; init; }
}

/// <summary>Un livreur a accepté la course. Le client peut être prévenu.</summary>
public sealed record DeliveryAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
}

/// <summary>Le colis est pris en charge : la course est physiquement engagée.</summary>
public sealed record DeliveryPickedUpIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
}

/// <summary>
/// Remise effectuée.
///
/// C'est l'événement le plus consommé du module : il clôt la commande côté
/// Ordering, déclenche le gain du livreur côté Wallet, et part en webhook chez
/// le partenaire. Toute modification de sa forme est une rupture de contrat
/// EXTERNE — pas seulement interne.
/// </summary>
public sealed record DeliveryCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
    public required DateTime DeliveredAtUtc { get; init; }

    /// <summary>
    /// Part revenant au livreur, figée à la remise. NULLE quand la course n'avait
    /// aucun prix — ce qui n'est pas « zéro » : « aucun gain calculé » se cherche,
    /// « zéro franc » se paie.
    /// </summary>
    public decimal? DriverEarning { get; init; }

    public string? Currency { get; init; }
}

/// <summary>Course annulée avant collecte.</summary>
public sealed record DeliveryCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Aucun livreur trouvé après épuisement des tentatives.
///
/// Destiné au DISPATCH HUMAIN et au donneur d'ordre — pas au client final. La
/// course reste vivante et reprenable : annoncer un échec à l'acheteur alors
/// qu'un opérateur peut encore la pourvoir ferait perdre une vente pour rien.
/// </summary>
public sealed record DeliveryNoDriverAvailableIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required int Attempts { get; init; }
}
