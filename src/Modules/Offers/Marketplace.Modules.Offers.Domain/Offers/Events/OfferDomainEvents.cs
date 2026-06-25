using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Offers.Domain.Offers.Events;

/// <summary>Une offre vient d'être créée (produit × vendeur).</summary>
public sealed record OfferCreatedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    decimal BasePriceAmount,
    string Currency) : DomainEvent;

/// <summary>Le prix de base d'une offre a changé.</summary>
public sealed record OfferPriceChangedDomainEvent(Guid OfferId, decimal Amount, string Currency) : DomainEvent;
