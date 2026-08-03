using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Offers.Contracts.IntegrationEvents;

/// <summary>Une offre a été créée. Consommé par Search (indexation), Pricing…</summary>
public sealed record OfferCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid OfferId { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required string Sku { get; init; }
    public required decimal BasePriceAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Le prix (montant et/ou devise) d'une offre a changé. Consommé par Search (réindexation prix/devise).</summary>
public sealed record OfferPriceChangedIntegrationEvent : IntegrationEvent
{
    public required Guid OfferId { get; init; }
    public required Guid ProductId { get; init; }
    public required decimal BasePriceAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// Une offre a été supprimée. Consommé par Search, qui affiche le prix « à partir de ».
///
/// L'événement PORTE le prix restant plutôt que de laisser Search le recalculer : le
/// module Search ne connaît pas les offres, et lui faire interroger Offers inverserait
/// la dépendance. C'est donc le module Offers, qui a la donnée sous la main au moment
/// de la suppression, qui la transmet.
///
/// <see cref="RemainingMinPriceAmount"/> et <see cref="Currency"/> sont NULS quand le
/// produit n'a plus aucune offre : Search efface alors le prix au lieu de conserver
/// celui d'une offre qui n'existe plus.
/// </summary>
public sealed record OfferDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid OfferId { get; init; }
    public required Guid ProductId { get; init; }
    public decimal? RemainingMinPriceAmount { get; init; }
    public string? Currency { get; init; }
}
