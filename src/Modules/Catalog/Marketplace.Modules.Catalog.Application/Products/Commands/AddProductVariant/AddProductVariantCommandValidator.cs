using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;

public sealed class AddProductVariantCommandValidator : AbstractValidator<AddProductVariantCommand>
{
    public AddProductVariantCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        // Le SKU est optionnel : vide, il est généré à partir de l'ID vendeur.
        // On ne valide la longueur que s'il est fourni.
        RuleFor(c => c.Sku).MaximumLength(64);
        RuleFor(c => c.WeightGrams).GreaterThanOrEqualTo(0);
    }
}
