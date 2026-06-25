using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Messaging.Contracts.IntegrationEvents;
using Marketplace.Modules.Messaging.Domain.Conversations.Events;

namespace Marketplace.Modules.Messaging.Application.Conversations.EventHandlers;

/// <summary>Publie « message envoyé » (Notifications alerte le destinataire).</summary>
public sealed class MessageSentDomainEventHandler : IDomainEventHandler<MessageSentDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MessageSentDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(MessageSentDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MessageSentIntegrationEvent
            {
                ConversationId = domainEvent.ConversationId,
                MessageId = domainEvent.MessageId,
                SenderId = domainEvent.SenderId
            },
            cancellationToken);
}
