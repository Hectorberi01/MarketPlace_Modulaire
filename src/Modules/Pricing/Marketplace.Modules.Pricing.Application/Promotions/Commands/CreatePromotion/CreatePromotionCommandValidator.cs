using FluentValidation;

namespace Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion;

public sealed class CreatePromotionCommandValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionCommandValidator()
    {
        RuleFor(c => c.OwnerType).Must(v => v is "Seller" or "Platform").WithMessage("OwnerType invalide.");
        RuleFor(c => c.OwnerId).NotEmpty();
        RuleFor(c => c.FundedBy).Must(v => v is "Seller" or "Platform" or "Shared").WithMessage("FundedBy invalide.");
        RuleFor(c => c.Type).Must(v => v is "Percentage" or "FixedAmount" or "FreeShipping" or "BuyXGetY").WithMessage("Type invalide.");
        RuleFor(c => c.ScopeType).Must(v => v is "Product" or "Category" or "Seller" or "Cart" or "Global").WithMessage("ScopeType invalide.");
        RuleFor(c => c.Value).GreaterThanOrEqualTo(0);
        RuleFor(c => c.UsageLimit).GreaterThanOrEqualTo(0);
        RuleFor(c => c.EndUtc).GreaterThan(c => c.StartUtc).WithMessage("La date de fin doit être postérieure à la date de début.");
    }
}
