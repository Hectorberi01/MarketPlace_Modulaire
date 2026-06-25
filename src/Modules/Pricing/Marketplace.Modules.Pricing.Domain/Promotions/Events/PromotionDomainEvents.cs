using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Pricing.Domain.Promotions.Events;

/// <summary>Une promotion a été créée.</summary>
public sealed record PromotionCreatedDomainEvent(Guid PromotionId, string Type, string ScopeType) : DomainEvent;

/// <summary>Une promotion a été activée.</summary>
public sealed record PromotionActivatedDomainEvent(Guid PromotionId) : DomainEvent;
