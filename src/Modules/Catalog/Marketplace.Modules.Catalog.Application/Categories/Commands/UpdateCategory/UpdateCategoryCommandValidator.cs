using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory;

/// <summary>Validation d'entrée de la mise à jour d'une catégorie.</summary>
public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(c => c.CategoryId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ImageUrl).MaximumLength(2000).When(c => !string.IsNullOrWhiteSpace(c.ImageUrl));
    }
}
