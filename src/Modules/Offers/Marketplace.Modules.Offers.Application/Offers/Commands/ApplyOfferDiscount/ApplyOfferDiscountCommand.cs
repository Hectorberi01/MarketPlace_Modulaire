using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ApplyOfferDiscount;

/// <summary>
/// Applique une remise (promo vendeur) à une offre. <paramref name="DiscountType"/> :
/// « Percentage » ou « Amount ». La commission est recalculée sur le prix réduit.
/// </summary>
public sealed record ApplyOfferDiscountCommand(
    Guid OfferId, string DiscountType, decimal Value, DateTime? EndsOnUtc) : ICommand;
