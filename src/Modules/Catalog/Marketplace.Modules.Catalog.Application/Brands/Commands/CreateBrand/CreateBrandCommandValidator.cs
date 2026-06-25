using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.CreateBrand;

public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LogoUrl).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl));
        RuleFor(c => c.Description).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.Description));
    }
}
