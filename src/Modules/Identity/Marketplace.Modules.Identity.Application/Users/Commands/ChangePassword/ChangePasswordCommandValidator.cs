using FluentValidation;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ChangePassword;

/// <summary>Validation d'entrée du changement de mot de passe.</summary>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.CurrentPassword).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
