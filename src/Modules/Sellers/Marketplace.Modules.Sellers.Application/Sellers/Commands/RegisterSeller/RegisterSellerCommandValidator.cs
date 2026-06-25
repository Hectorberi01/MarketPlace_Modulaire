using FluentValidation;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;

/// <summary>Validation d'entrée de l'onboarding vendeur.</summary>
public sealed class RegisterSellerCommandValidator : AbstractValidator<RegisterSellerCommand>
{
    public RegisterSellerCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.ShopName).NotEmpty().MaximumLength(150);
        RuleFor(c => c.CommissionRate).InclusiveBetween(0m, 1m);
    }
}
