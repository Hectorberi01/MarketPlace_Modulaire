using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.RemoveOfferDiscount;

/// <summary>Retire la remise d'une offre : restaure le prix vendeur d'origine.</summary>
public sealed record RemoveOfferDiscountCommand(Guid OfferId) : ICommand;
