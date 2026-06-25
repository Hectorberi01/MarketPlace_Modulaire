using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Domain.Users.Events;

namespace Marketplace.Modules.Identity.Application.Users.EventHandlers;

/// <summary>Publie l'event d'intégration « compte créé » (bienvenue, analytics…).</summary>
public sealed class UserRegisteredDomainEventHandler : IDomainEventHandler<UserRegisteredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserRegisteredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserRegisteredIntegrationEvent
            {
                UserId = domainEvent.UserId,
                Email = domainEvent.Email,
                FirstName = domainEvent.FirstName
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « e-mail confirmé ».</summary>
public sealed class UserEmailConfirmedDomainEventHandler : IDomainEventHandler<UserEmailConfirmedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserEmailConfirmedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserEmailConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserEmailConfirmedIntegrationEvent
            {
                UserId = domainEvent.UserId,
                Email = domainEvent.Email
            },
            cancellationToken);
}
