using FluentValidation;

namespace Marketplace.Modules.Identity.Application.Roles.Commands.CreateRole;

/// <summary>Validation d'entrée de la création d'un rôle.</summary>
public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Description).MaximumLength(500).When(c => !string.IsNullOrWhiteSpace(c.Description));
    }
}
