using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Reviews.Contracts.IntegrationEvents;

/// <summary>Un avis a été publié. Consommé par Search (mise à jour de la note produit).</summary>
public sealed record ReviewPublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Rating { get; init; }
}

/// <summary>Un avis a été rejeté. Consommé par Search.</summary>
public sealed record ReviewRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
}
