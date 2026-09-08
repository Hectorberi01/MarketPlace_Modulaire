namespace HbaExpress.Dispatch.Domain.Jobs;

public interface IDeliveryJobRepository
{
    Task AddAsync(DeliveryJob job, CancellationToken cancellationToken = default);

    Task<DeliveryJob?> GetByIdAsync(DeliveryJobId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// La course d'une commande. UNE SEULE : c'est le garde-fou contre les
    /// republications de « commande prête », que deux vendeurs terminant à la même
    /// seconde peuvent déclencher deux fois.
    /// </summary>
    Task<DeliveryJob?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses offertes dont la vague en cours est arrivée à échéance : à ré-offrir plus
    /// large, ou à faire expirer si le délai global est dépassé.
    /// </summary>
    Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default);
}
