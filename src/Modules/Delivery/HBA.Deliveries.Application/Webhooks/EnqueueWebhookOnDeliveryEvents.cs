using System.Text.Json;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Webhooks;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Application.Webhooks;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI MET LES WEBHOOKS EN FILE.
///
/// Ces handlers vivent DANS le module Deliveries, et cela ne viole pas la règle de
/// cloisonnement : ils ne consomment que les événements de Deliveries lui-même.
/// Le module reste sans dépendance vers aucun autre — c'est ce que vérifie
/// DeliveriesBoundaryTests.
///
/// UN SEUL FILTRE, ET IL EST DÉCISIF : LA SOURCE.
///
/// Seules les courses de source EXTERNE ont un partenaire à prévenir. Une course
/// HBAExpress mise en file produirait une ligne sans destinataire, réessayée six
/// fois pour rien, sur chacun des six événements de chaque course — soit
/// l'écrasante majorité du trafic de la file. C'est le genre de détail qui ne se
/// voit qu'en production, quand la table a dix millions de lignes mortes.
///
/// ⚠️ LE PARTENAIRE N'EST PAS DANS L'ÉVÉNEMENT.
///
/// Les événements portent la référence et la source, jamais un PartnerId : c'est
/// ce qui leur permet de servir aussi bien Ordering, la paie du livreur que les
/// webhooks. On relit donc la course pour savoir à qui écrire.
///
/// POURQUOI CES TYPES SONT « public » ET NON « internal »
///
/// Ils sont enregistrés dans le conteneur par DeliveriesModuleInstaller, qui vit
/// dans l'assembly Infrastructure : « internal » les y rend invisibles. C'est la
/// convention déjà retenue par ce module pour ses handlers d'événements de
/// domaine — la seule alternative serait un InternalsVisibleTo, qui ouvrirait
/// TOUT l'assembly Application à l'Infrastructure pour six classes.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryWebhookEnqueuer
{
    /// <summary>
    /// Sérialisation en camelCase : c'est ce qu'attend un intégrateur web, et le
    /// format est un CONTRAT EXTERNE. Le figer ici plutôt que de dépendre des
    /// réglages globaux de l'hôte évite qu'un changement de configuration de l'API
    /// modifie silencieusement la forme des webhooks déjà intégrés par des tiers.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDeliveryRepository _deliveries;
    private readonly IWebhookDeliveryRepository _webhooks;
    private readonly ILogger<DeliveryWebhookEnqueuer> _logger;

    public DeliveryWebhookEnqueuer(
        IDeliveryRepository deliveries,
        IWebhookDeliveryRepository webhooks,
        ILogger<DeliveryWebhookEnqueuer> logger)
    {
        _deliveries = deliveries;
        _webhooks = webhooks;
        _logger = logger;
    }

    public async Task EnqueueAsync<TEvent>(
        TEvent integrationEvent, Guid deliveryId, string source, string eventType, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        if (!string.Equals(source, nameof(DeliverySource.ExternalPartner), StringComparison.Ordinal))
        {
            return;
        }

        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(deliveryId), ct);
        if (delivery?.PartnerId is not { } partnerId)
        {
            // Une course externe SANS partenaire est un invariant rompu — l'agrégat
            // le refuse à la création. Si cela arrive, c'est une donnée corrompue,
            // et le signaler vaut mieux que de laisser le webhook disparaître.
            _logger.LogWarning(
                "Webhook non mis en file pour la course {DeliveryId} ({EventType}) : aucun partenaire rattaché.",
                deliveryId, eventType);

            return;
        }

        // Le corps est sérialisé UNE FOIS et figé : c'est exactement cet octet-là
        // qui sera signé, et un corps re-sérialisé à chaque tentative produirait
        // une signature différente pour le même événement.
        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), PayloadFormat);

        var webhook = WebhookDelivery.Enqueue(partnerId, integrationEvent.Id, eventType, payload);
        if (webhook.IsFailure)
        {
            _logger.LogError(
                "Webhook non mis en file pour la course {DeliveryId} : {Code}.",
                deliveryId, webhook.Error.Code);

            return;
        }

        await _webhooks.AddAsync(webhook.Value, ct);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// UN HANDLER PAR ÉVÉNEMENT.
//
// Ils sont volontairement répétitifs et sans logique : toute la décision est dans
// l'enqueueur. Un handler générique unique aurait demandé une réflexion sur le
// type pour retrouver « quel est l'identifiant de course » — c'est-à-dire du code
// qui casse en silence quand un événement change de forme, au lieu de casser à la
// compilation.
//
// Les NOMS D'ÉVÉNEMENT sont des chaînes stables, en minuscules pointées. C'est ce
// que le partenaire branche dans son « switch » : les dériver du nom de classe C#
// ferait d'un renommage interne une rupture de contrat externe.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class WebhookOnDeliveryCreated : IIntegrationEventHandler<DeliveryCreatedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCreated(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCreatedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.created", ct);
}

public sealed class WebhookOnDeliveryAccepted : IIntegrationEventHandler<DeliveryAcceptedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryAccepted(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryAcceptedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.accepted", ct);
}

public sealed class WebhookOnDeliveryPickedUp : IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryPickedUp(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryPickedUpIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.picked_up", ct);
}

public sealed class WebhookOnDeliveryCompleted : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCompleted(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCompletedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.completed", ct);
}

public sealed class WebhookOnDeliveryCancelled : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCancelled(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCancelledIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.cancelled", ct);
}

public sealed class WebhookOnDeliveryNoDriver : IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryNoDriver(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryNoDriverAvailableIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.no_driver_available", ct);
}
