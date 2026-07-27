using MediatR;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;

namespace Marketplace.Modules.Reviews.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Reviews.</summary>
internal sealed class ReviewsModuleApi : IReviewsModuleApi
{
    private readonly ISender _sender;

    public ReviewsModuleApi(ISender sender) => _sender = sender;

    public async Task<ProductRatingSummary> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetProductRatingQuery(productId), cancellationToken);
        return result.IsSuccess ? result.Value : new ProductRatingSummary(productId, 0d, 0);
    }

    public async Task<SellerRatingSummary> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetSellerRatingQuery(sellerId), cancellationToken);
        return result.IsSuccess ? result.Value : new SellerRatingSummary(sellerId, 0d, 0);
    }
}
