using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Disputes.Contracts.IntegrationEvents;

/// <summary>Un litige a été ouvert. Consommé par Settlement/Payments (gel de l'escrow) et Notifications.</summary>
public sealed record DisputeOpenedIntegrationEvent : IntegrationEvent
{
    public required Guid DisputeId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RaisedBy { get; init; }
    public required string Type { get; init; }
}

/// <summary>Un litige a été tranché. Consommé par Settlement/Payments (libération de l'escrow) et Notifications.</summary>
public sealed record DisputeResolvedIntegrationEvent : IntegrationEvent
{
    public required Guid DisputeId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Resolution { get; init; }
}
