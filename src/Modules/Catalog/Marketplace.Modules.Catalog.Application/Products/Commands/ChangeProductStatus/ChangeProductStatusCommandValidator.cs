using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>Vérifie que le statut cible est l'une des valeurs autorisées.</summary>
public sealed class ChangeProductStatusCommandValidator : AbstractValidator<ChangeProductStatusCommand>
{
    public ChangeProductStatusCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Status)
            .Must(s => s is "Active" or "Archived" or "Draft")
            .WithMessage("Statut invalide (Active, Archived ou Draft).");
    }
}
