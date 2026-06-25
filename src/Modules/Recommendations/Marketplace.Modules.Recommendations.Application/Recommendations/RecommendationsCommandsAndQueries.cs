using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Recommendations.Contracts;
using Marketplace.Modules.Recommendations.Domain.Recommendations;

namespace Marketplace.Modules.Recommendations.Application.Recommendations;

/// <summary>Unit of Work propre au module Recommendations.</summary>
public interface IRecommendationsUnitOfWork : IUnitOfWork
{
}

/// <summary>Crée ou rafraîchit une recommandation (batch / moteur de règles).</summary>
public sealed record UpsertRecommendationCommand(
    string Type, Guid? ContextProductId, Guid? UserId, IReadOnlyList<Guid> RecommendedProductIds, double Score) : ICommand<Guid>;

/// <summary>Recommandations associées à un produit (Similar / FrequentlyBoughtTogether).</summary>
public sealed record GetProductRecommendationsQuery(Guid ProductId, string Type) : IQuery<RecommendationSummary>;

/// <summary>Recommandations personnalisées d'un utilisateur.</summary>
public sealed record GetUserRecommendationsQuery(Guid UserId) : IQuery<RecommendationSummary>;

internal static class RecommendationMapper
{
    public static RecommendationSummary ToSummary(Recommendation r) => new(
        r.Id, r.Type.ToString(), r.ContextProductId, r.UserId, r.RecommendedProductIds.ToList(), r.Score, r.GeneratedAtUtc);
}

internal sealed class UpsertRecommendationCommandHandler : ICommandHandler<UpsertRecommendationCommand, Guid>
{
    private readonly IRecommendationRepository _repository;
    private readonly IRecommendationsUnitOfWork _unitOfWork;

    public UpsertRecommendationCommandHandler(IRecommendationRepository repository, IRecommendationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(UpsertRecommendationCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RecommendationType>(command.Type, ignoreCase: true, out var type))
        {
            return Result.Failure<Guid>(Error.Validation("recommendations.type_invalid", "Type de recommandation inconnu."));
        }

        Recommendation? existing = type == RecommendationType.Personalized
            ? command.UserId is { } uid ? await _repository.GetByUserAsync(type, uid, cancellationToken) : null
            : command.ContextProductId is { } pid ? await _repository.GetByProductAsync(type, pid, cancellationToken) : null;

        if (existing is not null)
        {
            existing.Refresh(command.RecommendedProductIds, command.Score);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var recommendation = Recommendation.Create(type, command.ContextProductId, command.UserId, command.RecommendedProductIds, command.Score);
        await _repository.AddAsync(recommendation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return recommendation.Id;
    }
}

internal sealed class GetProductRecommendationsQueryHandler : IQueryHandler<GetProductRecommendationsQuery, RecommendationSummary>
{
    private readonly IRecommendationRepository _repository;
    public GetProductRecommendationsQueryHandler(IRecommendationRepository repository) => _repository = repository;

    public async Task<Result<RecommendationSummary>> Handle(GetProductRecommendationsQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RecommendationType>(query.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("recommendations.type_invalid", "Type de recommandation inconnu.");
        }

        var recommendation = await _repository.GetByProductAsync(type, query.ProductId, cancellationToken);
        return recommendation is null
            ? new RecommendationSummary(Guid.Empty, type.ToString(), query.ProductId, null, Array.Empty<Guid>(), 0d, DateTime.MinValue)
            : RecommendationMapper.ToSummary(recommendation);
    }
}

internal sealed class GetUserRecommendationsQueryHandler : IQueryHandler<GetUserRecommendationsQuery, RecommendationSummary>
{
    private readonly IRecommendationRepository _repository;
    public GetUserRecommendationsQueryHandler(IRecommendationRepository repository) => _repository = repository;

    public async Task<Result<RecommendationSummary>> Handle(GetUserRecommendationsQuery query, CancellationToken cancellationToken)
    {
        var recommendation = await _repository.GetByUserAsync(RecommendationType.Personalized, query.UserId, cancellationToken);
        return recommendation is null
            ? new RecommendationSummary(Guid.Empty, RecommendationType.Personalized.ToString(), null, query.UserId, Array.Empty<Guid>(), 0d, DateTime.MinValue)
            : RecommendationMapper.ToSummary(recommendation);
    }
}
