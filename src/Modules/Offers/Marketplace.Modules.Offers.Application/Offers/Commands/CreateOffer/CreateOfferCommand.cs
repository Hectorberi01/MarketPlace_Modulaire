using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;

/// <summary>Crée une offre (vendeur actif × produit existant) sur une variante.</summary>
public sealed record CreateOfferCommand(
    Guid ProductId,
    Guid SellerId,
    string Sku,
    decimal SellerPrice,
    string Currency,
    string Condition,
    string FulfillmentType,
    Guid ShipFromLocationId,
    int HandlingTime) : ICommand<Guid>;
