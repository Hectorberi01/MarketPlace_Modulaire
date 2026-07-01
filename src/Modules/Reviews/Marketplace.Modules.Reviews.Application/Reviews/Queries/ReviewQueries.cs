using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Application.Reviews.Queries;

/// <summary>Récupère un avis par son identifiant.</summary>
public sealed record GetReviewQuery(Guid ReviewId) : IQuery<ReviewSummary>;

/// <summary>Liste les avis publiés d'un produit.</summary>
public sealed record ListReviewsByProductQuery(Guid ProductId) : IQuery<IReadOnlyList<ReviewSummary>>;

/// <summary>Note agrégée d'un produit (moyenne + nombre d'avis publiés).</summary>
public sealed record GetProductRatingQuery(Guid ProductId) : IQuery<ProductRatingSummary>;

/// <summary>Liste tous les avis ciblant les produits d'un vendeur (back-office vendeur).</summary>
public sealed record ListReviewsBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<ReviewSummary>>;

internal sealed class ListReviewsBySellerQueryHandler : IQueryHandler<ListReviewsBySellerQuery, IReadOnlyList<ReviewSummary>>
{
    private readonly IReviewRepository _repository;

    public ListReviewsBySellerQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ReviewSummary>>> Handle(ListReviewsBySellerQuery query, CancellationToken cancellationToken)
    {
        var reviews = await _repository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<ReviewSummary> summaries = reviews.Select(ReviewMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetReviewQueryHandler : IQueryHandler<GetReviewQuery, ReviewSummary>
{
    private readonly IReviewRepository _repository;

    public GetReviewQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<ReviewSummary>> Handle(GetReviewQuery query, CancellationToken cancellationToken)
    {
        var review = await _repository.GetByIdAsync(new ReviewId(query.ReviewId), cancellationToken);
        return review is null
            ? Error.NotFound("reviews.not_found", "Avis introuvable.")
            : ReviewMapper.ToSummary(review);
    }
}

internal sealed class ListReviewsByProductQueryHandler : IQueryHandler<ListReviewsByProductQuery, IReadOnlyList<ReviewSummary>>
{
    private readonly IReviewRepository _repository;

    public ListReviewsByProductQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ReviewSummary>>> Handle(ListReviewsByProductQuery query, CancellationToken cancellationToken)
    {
        var reviews = await _repository.ListByProductAsync(query.ProductId, cancellationToken);
        IReadOnlyList<ReviewSummary> summaries = reviews.Select(ReviewMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetProductRatingQueryHandler : IQueryHandler<GetProductRatingQuery, ProductRatingSummary>
{
    private readonly IReviewRepository _repository;

    public GetProductRatingQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<ProductRatingSummary>> Handle(GetProductRatingQuery query, CancellationToken cancellationToken)
    {
        var rating = await _repository.GetProductRatingAsync(query.ProductId, cancellationToken);
        return new ProductRatingSummary(rating.ProductId, rating.Average, rating.Count);
    }
}
