using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Delivery.Contracts.IntegrationEvents;

/// <summary>Un coursier a été vérifié : il peut recevoir des courses et être payé.</summary>
public sealed record CourierActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid CourierId { get; init; }
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Un coursier a été suspendu. Le service de dispatch doit cesser de lui offrir des
/// courses — c'est lui qui tient la disponibilité, la marketplace ne fait que décider
/// qui a le droit d'être payé.
/// </summary>
public sealed record CourierSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid CourierId { get; init; }
    public required string Reason { get; init; }
}
