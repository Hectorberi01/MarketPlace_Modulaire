using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Settlement.Domain.Batches.Events;

/// <summary>Un reversement a été versé à un vendeur (consommé par Notifications / compta).</summary>
public sealed record PayoutPaidDomainEvent(Guid BatchId, Guid PayoutId, Guid SellerId, decimal NetAmount, string Currency) : DomainEvent;
