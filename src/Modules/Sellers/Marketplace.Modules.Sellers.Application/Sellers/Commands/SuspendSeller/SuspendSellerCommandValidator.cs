using FluentValidation;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;

public sealed class SuspendSellerCommandValidator : AbstractValidator<SuspendSellerCommand>
{
    public SuspendSellerCommandValidator()
    {
        RuleFor(c => c.SellerId).NotEmpty();

        // Le vendeur reçoit ce texte tel quel. Trop court, il n'apprend rien ; c'est
        // exactement le défaut du rejet de KYB, muet et sans motif, que le vendeur
        // « corrige » en re-téléversant la même pièce.
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Le motif de la suspension est obligatoire : le vendeur le recevra.")
            .MinimumLength(10).WithMessage("Le motif doit être compréhensible par le vendeur (10 caractères au minimum).")
            .MaximumLength(1000);
    }
}
