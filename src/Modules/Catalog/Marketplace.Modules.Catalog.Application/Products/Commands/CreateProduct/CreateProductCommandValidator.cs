using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Validation d'entrée (forme), exécutée par le ValidationBehavior avant le
/// handler. Les invariants métier profonds restent dans l'agrégat Product.
/// </summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(c => c.SellerId).NotEmpty();
        RuleFor(c => c.CategoryId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.Gtin).MaximumLength(14).When(c => !string.IsNullOrWhiteSpace(c.Gtin));
    }
}
