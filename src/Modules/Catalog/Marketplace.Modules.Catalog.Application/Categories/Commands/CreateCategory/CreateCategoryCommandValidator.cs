using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.CreateCategory;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ImageUrl).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.ImageUrl));
    }
}
