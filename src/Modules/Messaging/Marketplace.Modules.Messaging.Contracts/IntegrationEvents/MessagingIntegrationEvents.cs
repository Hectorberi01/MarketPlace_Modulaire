using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Messaging.Contracts.IntegrationEvents;

/// <summary>Un message a été envoyé. Consommé par Notifications (alerte temps réel au destinataire).</summary>
public sealed record MessageSentIntegrationEvent : IntegrationEvent
{
    public required Guid ConversationId { get; init; }
    public required Guid MessageId { get; init; }
    public required Guid SenderId { get; init; }
}
