using FluentValidation;

namespace Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;

public sealed class InitiatePaymentCommandValidator : AbstractValidator<InitiatePaymentCommand>
{
    public InitiatePaymentCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Method).NotEmpty();
        RuleFor(c => c.Provider).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Flow).NotEmpty();
        RuleFor(c => c.ReturnUrl).MaximumLength(2000);
        RuleFor(c => c.CancelUrl).MaximumLength(2000);
    }
}
