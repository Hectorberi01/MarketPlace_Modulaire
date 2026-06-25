namespace Marketplace.Modules.Analytics.Domain.Metrics;

/// <summary>
/// Marqueur d'idempotence : trace les commandes déjà comptabilisées pour éviter
/// le double-comptage en cas de relivraison d'un événement par l'outbox.
/// </summary>
public sealed class ProcessedOrder
{
    private ProcessedOrder()
    {
    }

    private ProcessedOrder(Guid orderId)
    {
        OrderId = orderId;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }

    public static ProcessedOrder Mark(Guid orderId) => new(orderId);
}
