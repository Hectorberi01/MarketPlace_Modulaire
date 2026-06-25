using FluentValidation;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Login;

/// <summary>Validation d'entrée du login.</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}
