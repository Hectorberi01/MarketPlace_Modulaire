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
        // GTIN ET EAN. L'EAN ÉTAIT OUBLIÉ ICI, ET L'OUBLI SE VOYAIT MAL.
        //
        // Deux champs de même type, de même longueur, stockés dans deux colonnes
        // identiques (`HasMaxLength(14)` pour les deux) : un contrôle qui n'en couvre
        // qu'un est plus trompeur qu'un contrôle absent, parce qu'on le croit complet.
        RuleFor(c => c.Gtin).MaximumLength(14).When(c => !string.IsNullOrWhiteSpace(c.Gtin));
        RuleFor(c => c.Ean).MaximumLength(14).When(c => !string.IsNullOrWhiteSpace(c.Ean));
    }
}
