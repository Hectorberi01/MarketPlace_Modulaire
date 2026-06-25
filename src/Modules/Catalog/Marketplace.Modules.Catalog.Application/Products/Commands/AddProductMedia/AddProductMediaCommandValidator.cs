using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;

public sealed class AddProductMediaCommandValidator : AbstractValidator<AddProductMediaCommand>
{
    public AddProductMediaCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Url).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.Type)
            .Must(t => t is "Image" or "Video")
            .WithMessage("Type de média invalide (Image ou Video).");
    }
}
