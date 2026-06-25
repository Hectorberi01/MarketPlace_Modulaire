using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;

/// <summary>Change le prix de base d'une offre.</summary>
public sealed record ChangeOfferPriceCommand(Guid OfferId, decimal Amount, string Currency) : ICommand;
