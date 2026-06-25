using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.UpdateBrand;

/// <summary>Validation d'entrée de la mise à jour d'une marque.</summary>
public sealed class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandCommandValidator()
    {
        RuleFor(c => c.BrandId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LogoUrl).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl));
        RuleFor(c => c.Description).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.Description));
    }
}
