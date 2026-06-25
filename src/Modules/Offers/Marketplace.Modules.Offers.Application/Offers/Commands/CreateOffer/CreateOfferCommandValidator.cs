using FluentValidation;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;

public sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.SellerId).NotEmpty();
        RuleFor(c => c.Sku).NotEmpty().MaximumLength(64);
        RuleFor(c => c.BasePriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ShipFromLocationId).NotEmpty();
        RuleFor(c => c.HandlingTime).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Condition).Must(v => v is "New" or "Used" or "Refurbished")
            .WithMessage("Condition invalide (New, Used, Refurbished).");
        RuleFor(c => c.FulfillmentType).Must(v => v is "Fbs" or "Fbp")
            .WithMessage("Mode de fulfillment invalide (Fbs, Fbp).");
    }
}
