using FluentValidation;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;

public sealed class RejectKybCommandValidator : AbstractValidator<RejectKybCommand>
{
    public RejectKybCommandValidator()
    {
        RuleFor(c => c.SellerId).NotEmpty();

        // Le vendeur reçoit ce texte tel quel : « non » n'est pas un motif.
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Le motif du refus est obligatoire : le vendeur le recevra.")
            .MinimumLength(10).WithMessage("Le motif doit dire ce qui ne va pas (10 caractères au minimum).")
            .MaximumLength(1000);
    }
}
