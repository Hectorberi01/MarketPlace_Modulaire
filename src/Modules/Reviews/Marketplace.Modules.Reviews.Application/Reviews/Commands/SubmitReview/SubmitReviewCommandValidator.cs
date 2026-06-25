using FluentValidation;

namespace Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;

public sealed class SubmitReviewCommandValidator : AbstractValidator<SubmitReviewCommand>
{
    public SubmitReviewCommandValidator()
    {
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Rating).InclusiveBetween(1, 5);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
        RuleFor(c => c.Title).MaximumLength(200);
    }
}
