using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;

public sealed class AddProductVariantCommandValidator : AbstractValidator<AddProductVariantCommand>
{
    public AddProductVariantCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Sku).NotEmpty().MaximumLength(64);
        RuleFor(c => c.WeightGrams).GreaterThanOrEqualTo(0);
    }
}
