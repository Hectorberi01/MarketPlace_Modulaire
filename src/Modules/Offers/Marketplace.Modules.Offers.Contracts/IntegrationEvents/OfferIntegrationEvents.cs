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
