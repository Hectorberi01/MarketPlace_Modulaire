using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Application.Reviews;

internal static class ReviewMapper
{
    public static ReviewSummary ToSummary(Review r) => new(
        r.Id.Value,
        r.ProductId,
        r.SellerId,
        r.BuyerId,
        r.Rating.Value,
        r.Title,
        r.Body,
        r.IsVerifiedPurchase,
        r.Status.ToString(),
        r.CreatedAtUtc);
}
