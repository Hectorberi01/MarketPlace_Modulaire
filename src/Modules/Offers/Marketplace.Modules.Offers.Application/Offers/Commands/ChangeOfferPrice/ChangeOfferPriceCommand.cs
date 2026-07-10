using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;

/// <summary>
/// Change le prix d'une offre. <paramref name="SellerPrice"/> est le PRIX VENDEUR net ;
/// la plateforme recalcule commission, frais provider et prix produit. La devise reste
/// celle de l'offre existante (BasePrice), non modifiable ici.
/// </summary>
public sealed record ChangeOfferPriceCommand(Guid OfferId, decimal SellerPrice) : ICommand;
