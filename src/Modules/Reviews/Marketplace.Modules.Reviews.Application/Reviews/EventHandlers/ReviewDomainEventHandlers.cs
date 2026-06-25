using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Reviews.Domain.Reviews.Events;

namespace Marketplace.Modules.Reviews.Application.Reviews.EventHandlers;

/// <summary>Publie « avis publié » (Search met à jour la note du produit).</summary>
public sealed class ReviewPublishedDomainEventHandler : IDomainEventHandler<ReviewPublishedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ReviewPublishedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ReviewPublishedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReviewPublishedIntegrationEvent { ReviewId = domainEvent.ReviewId, ProductId = domainEvent.ProductId, Rating = domainEvent.Rating },
            cancellationToken);
}

/// <summary>Publie « avis rejeté » (Search retire sa contribution à la note).</summary>
public sealed class ReviewRejectedDomainEventHandler : IDomainEventHandler<ReviewRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ReviewRejectedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(ReviewRejectedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReviewRejectedIntegrationEvent { ReviewId = domainEvent.ReviewId, ProductId = domainEvent.ProductId },
            cancellationToken);
}
