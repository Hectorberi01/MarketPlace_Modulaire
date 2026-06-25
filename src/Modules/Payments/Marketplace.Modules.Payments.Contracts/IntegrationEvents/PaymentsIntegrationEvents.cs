using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>Paiement encaissé. Consommé par Ordering (confirmation de la commande).</summary>
public sealed record PaymentCapturedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
}

/// <summary>Paiement échoué. Consommé par Ordering (annulation + libération du stock).</summary>
public sealed record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Paiement remboursé. Consommé par Notifications / comptabilité.</summary>
public sealed record PaymentRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
}
