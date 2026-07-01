using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Domain.Offers.Events;

namespace Marketplace.Modules.Offers.Domain.Offers;

/// <summary>
/// Le pont produit × vendeur : un vendeur propose une variante à un prix de base,
/// dans un état de condition et un mode de fulfillment (cf. dossier, module Offers).
/// Le prix de base est immuable côté promo (Pricing empile dessus) — ici on peut
/// le changer explicitement.
/// </summary>
public sealed class Offer : AggregateRoot<OfferId>
{
    private Offer()
    {
    }

    private Offer(
        OfferId id,
        Guid productId,
        Guid sellerId,
        Sku variantSku,
        Money basePrice,
        OfferCondition condition,
        FulfillmentType fulfillmentType,
        Guid shipFromLocationId,
        int handlingTime)
        : base(id)
    {
        ProductId = productId;
        SellerId = sellerId;
        VariantSku = variantSku;
        BasePrice = basePrice;
        Condition = condition;
        FulfillmentType = fulfillmentType;
        ShipFromLocationId = shipFromLocationId;
        HandlingTime = handlingTime;
        Status = OfferStatus.Active;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new OfferCreatedDomainEvent(id.Value, productId, sellerId, variantSku.Value, basePrice.Amount, basePrice.Currency));
    }

    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public Sku VariantSku { get; private set; } = default!;
    public Money BasePrice { get; private set; } = default!;
    public OfferCondition Condition { get; private set; }
    public FulfillmentType FulfillmentType { get; private set; }
    public Guid ShipFromLocationId { get; private set; }
    public int HandlingTime { get; private set; }
    public OfferStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static Result<Offer> Create(
        Guid productId,
        Guid sellerId,
        Sku variantSku,
        Money basePrice,
        OfferCondition condition,
        FulfillmentType fulfillmentType,
        Guid shipFromLocationId,
        int handlingTime)
    {
        if (productId == Guid.Empty)
        {
            return Error.Validation("offers.offer.product_required", "Le produit est obligatoire.");
        }

        if (sellerId == Guid.Empty)
        {
            return Error.Validation("offers.offer.seller_required", "Le vendeur est obligatoire.");
        }

        if (shipFromLocationId == Guid.Empty)
        {
            return Error.Validation("offers.offer.location_required", "La localisation d'expédition est obligatoire.");
        }

        if (handlingTime < 0)
        {
            return Error.Validation("offers.offer.handling_invalid", "Le délai de préparation ne peut pas être négatif.");
        }

        return new Offer(OfferId.New(), productId, sellerId, variantSku, basePrice, condition, fulfillmentType, shipFromLocationId, handlingTime);
    }

    public Result ChangePrice(Money newPrice)
    {
        BasePrice = newPrice;
        Raise(new OfferPriceChangedDomainEvent(Id.Value, ProductId, newPrice.Amount, newPrice.Currency));
        return Result.Success();
    }

    public Result SetHandlingTime(int handlingTime)
    {
        if (handlingTime < 0)
        {
            return Result.Failure(Error.Validation("offers.offer.handling_invalid", "Le délai de préparation ne peut pas être négatif."));
        }

        HandlingTime = handlingTime;
        return Result.Success();
    }

    public Result ChangeShipFromLocation(Guid shipFromLocationId)
    {
        if (shipFromLocationId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("offers.offer.location_required", "La localisation d'expédition est obligatoire."));
        }

        ShipFromLocationId = shipFromLocationId;
        return Result.Success();
    }

    public Result Activate()
    {
        Status = OfferStatus.Active;
        return Result.Success();
    }

    public Result Pause()
    {
        Status = OfferStatus.Paused;
        return Result.Success();
    }

    public Result MarkOutOfStock()
    {
        Status = OfferStatus.OutOfStock;
        return Result.Success();
    }
}
