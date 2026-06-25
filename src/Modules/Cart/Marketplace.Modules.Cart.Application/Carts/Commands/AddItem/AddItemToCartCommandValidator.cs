using FluentValidation;

namespace Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;

public sealed class AddItemToCartCommandValidator : AbstractValidator<AddItemToCartCommand>
{
    public AddItemToCartCommandValidator()
    {
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.OfferId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
    }
}
