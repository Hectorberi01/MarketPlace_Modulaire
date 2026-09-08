using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Delivery.Contracts.IntegrationEvents;
using Marketplace.Modules.Delivery.Domain.Couriers;
using Marketplace.Modules.Delivery.Domain.Couriers.Events;

namespace Marketplace.Modules.Delivery.Application.Couriers;

/// <summary>
/// Annonce l'activation d'un coursier au reste du monde — et surtout au service de
/// dispatch, qui ne doit offrir des courses qu'à des coursiers payables.
/// </summary>
public sealed class CourierActivatedDomainEventHandler : IDomainEventHandler<CourierActivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ICourierRepository _couriers;

    public CourierActivatedDomainEventHandler(IIntegrationEventPublisher publisher, ICourierRepository couriers)
    {
        _publisher = publisher;
        _couriers = couriers;
    }

    public async Task HandleAsync(CourierActivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(domainEvent.CourierId), cancellationToken);
        if (courier is null)
        {
            return;
        }

        await _publisher.PublishAsync(
            new CourierActivatedIntegrationEvent
            {
                CourierId = courier.Id.Value,
                UserId = courier.UserId,
                DisplayName = courier.DisplayName
            },
            cancellationToken);
    }
}

/// <summary>
/// Annonce la suspension. Le dispatch doit cesser de lui offrir des courses : c'est lui
/// qui tient la disponibilité, la marketplace ne décide que du droit d'être payé.
/// </summary>
public sealed class CourierSuspendedDomainEventHandler : IDomainEventHandler<CourierSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ICourierRepository _couriers;

    public CourierSuspendedDomainEventHandler(
        IIntegrationEventPublisher publisher, ICourierRepository couriers)
    {
        _publisher = publisher;
        _couriers = couriers;
    }

    // LE COURSIER EST RELU POUR SON COMPTE UTILISATEUR, comme à l'activation.
    // L'événement de domaine ne porte que l'identifiant de bénéficiaire ; le dispatch a
    // besoin des deux, sans quoi un coursier qu'il découvrirait par cette annonce serait
    // créé sans moyen de s'authentifier.
    public async Task HandleAsync(
        CourierSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(domainEvent.CourierId), cancellationToken);
        if (courier is null)
        {
            return;
        }

        await _publisher.PublishAsync(
            new CourierSuspendedIntegrationEvent
            {
                CourierId = courier.Id.Value,
                UserId = courier.UserId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
    }
}
