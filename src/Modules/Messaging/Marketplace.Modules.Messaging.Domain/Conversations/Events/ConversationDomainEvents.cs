using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Messaging.Domain.Conversations.Events;

/// <summary>Une conversation a été démarrée.</summary>
public sealed record ConversationStartedDomainEvent(Guid ConversationId, Guid StartedBy) : DomainEvent;

/// <summary>Un message a été envoyé (consommé par Notifications temps réel).</summary>
public sealed record MessageSentDomainEvent(Guid ConversationId, Guid MessageId, Guid SenderId) : DomainEvent;
