using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>Validation d'entrée de la mise à jour d'un produit.</summary>
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.Gtin).MaximumLength(14).When(c => !string.IsNullOrWhiteSpace(c.Gtin));
        RuleFor(c => c.Ean).MaximumLength(14).When(c => !string.IsNullOrWhiteSpace(c.Ean));
    }
}
