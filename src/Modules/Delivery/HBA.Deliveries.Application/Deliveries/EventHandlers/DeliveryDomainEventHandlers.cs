using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Domain.Deliveries.Events;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace HBA.Deliveries.Application.Deliveries.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TRADUCTION DES FAITS INTERNES EN FAITS PUBLICS.
///
/// TOUS LES ÉVÉNEMENTS DE DOMAINE NE SORTENT PAS.
///
/// Ceux du dispatch — proposition faite, refusée, recherche relancée — restent à
/// l'intérieur du module : ils décrivent une mécanique, pas un fait sur lequel un
/// tiers agit. Les publier produirait quatre à cinq messages par course, que
/// l'outbox devrait écrire, relayer et conserver, et que personne ne consomme.
///
/// Ne sortent que les faits actionnables : créée, acceptée, collectée, livrée,
/// annulée, et l'appel au dispatch humain.
///
/// CHAQUE TRADUCTEUR EST UNE SIMPLE RECOPIE, ET C'EST VOULU.
///
/// Une première version faisait relire la course en base pour retrouver sa
/// référence, que certains événements de domaine ne portaient pas. C'était une
/// lecture de plus par événement, et surtout un mode de défaillance nouveau sur
/// un chemin où l'échec signifie « le client ne sera jamais prévenu ». Les
/// événements de domaine portent désormais leur référence : il ne reste ici
/// aucune dépendance, aucun appel, rien qui puisse échouer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryCreatedDomainEventHandler : IDomainEventHandler<DeliveryCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCreatedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCreatedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Type = domainEvent.Type.ToString()
            },
            cancellationToken);
}

/// <summary>Un livreur a accepté : le client peut être prévenu.</summary>
public sealed class DeliveryAcceptedDomainEventHandler : IDomainEventHandler<DeliveryAcceptedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryAcceptedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryAcceptedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryAcceptedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>Colis pris en charge : la course est physiquement engagée.</summary>
public sealed class DeliveryPickedUpDomainEventHandler : IDomainEventHandler<DeliveryPickedUpDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryPickedUpDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryPickedUpDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryPickedUpIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>
/// Remise effectuée — l'événement le plus consommé du module : il clôt la
/// commande, déclenche le gain du livreur et part en webhook partenaire.
/// </summary>
public sealed class DeliveryCompletedDomainEventHandler : IDomainEventHandler<DeliveryCompletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCompletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCompletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCompletedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId,
                DeliveredAtUtc = domainEvent.DeliveredAtUtc,
                DriverEarning = domainEvent.DriverEarning,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Annulation avant collecte.</summary>
public sealed class DeliveryCancelledDomainEventHandler : IDomainEventHandler<DeliveryCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCancelledIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Aucun livreur trouvé : appel au dispatch humain, pas au client final.</summary>
public sealed class DeliveryNoDriverAvailableDomainEventHandler
    : IDomainEventHandler<DeliveryNoDriverAvailableDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryNoDriverAvailableDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        DeliveryNoDriverAvailableDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryNoDriverAvailableIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Attempts = domainEvent.Attempts
            },
            cancellationToken);
}
