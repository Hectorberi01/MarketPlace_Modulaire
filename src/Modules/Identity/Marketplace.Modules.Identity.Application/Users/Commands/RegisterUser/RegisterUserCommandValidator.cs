using FluentValidation;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

/// <summary>Validation d'entrée de l'inscription (forme ; les VOs valident le fond).</summary>
public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(320);
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
