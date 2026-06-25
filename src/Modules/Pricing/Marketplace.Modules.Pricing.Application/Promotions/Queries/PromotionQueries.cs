using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Contracts;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Promotions.Queries;

/// <summary>Récupère une promotion par son identifiant.</summary>
public sealed record GetPromotionQuery(Guid PromotionId) : IQuery<PromotionSummary>;

/// <summary>Liste les promotions (back-office).</summary>
public sealed record ListPromotionsQuery : IQuery<IReadOnlyList<PromotionSummary>>;

internal sealed class GetPromotionQueryHandler : IQueryHandler<GetPromotionQuery, PromotionSummary>
{
    private readonly IPromotionRepository _repository;

    public GetPromotionQueryHandler(IPromotionRepository repository) => _repository = repository;

    public async Task<Result<PromotionSummary>> Handle(GetPromotionQuery query, CancellationToken cancellationToken)
    {
        var promotion = await _repository.GetByIdAsync(new PromotionId(query.PromotionId), cancellationToken);
        return promotion is null
            ? Error.NotFound("pricing.promotion.not_found", "Promotion introuvable.")
            : PromotionMapper.ToSummary(promotion);
    }
}

internal sealed class ListPromotionsQueryHandler : IQueryHandler<ListPromotionsQuery, IReadOnlyList<PromotionSummary>>
{
    private readonly IPromotionRepository _repository;

    public ListPromotionsQueryHandler(IPromotionRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<PromotionSummary>>> Handle(ListPromotionsQuery query, CancellationToken cancellationToken)
    {
        var promotions = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<PromotionSummary> summaries = promotions.Select(PromotionMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
