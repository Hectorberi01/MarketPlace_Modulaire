using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Domain.Shipments;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;

namespace Marketplace.Modules.Shipping.Application.Shipments.EventHandlers;

/// <summary>
/// Annonce qu'une commande est prête à partir, dès que le DERNIER vendeur a préparé sa
/// part — et assemble le brief complet que le service coursier utilisera pour rouler.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI « LE DERNIER », ET PAS « UN VENDEUR A PRÉPARÉ ».
///
/// Une commande multi-vendeur envoie le coursier chez plusieurs boutiques avant de
/// livrer l'acheteur. Déclencher dès la première part le ferait attendre devant les
/// suivantes — et cette attente se paie, en temps de coursier comme en retard client.
///
/// POURQUOI CE HANDLER DÉPEND DE QUATRE MODULES.
///
/// Le service coursier est déployé à part et ne touche pas aux bases de la marketplace.
/// Lui envoyer des identifiants l'obligerait à rappeler l'API pour chaque adresse, au
/// moment précis où il doit dispatcher en moins d'une seconde. On assemble donc ici, une
/// fois : adresse acheteur (Ordering), contact vendeur (Sellers + Identity), adresse de
/// ramassage (Inventory). Uniquement par leurs Contracts, jamais par leurs bases.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// CET ÉVÉNEMENT N'A PLUS AUCUN CONSOMMATEUR, ET C'EST VOULU (09/10/2026).
///
/// Il était consommé par `SendOrderReadyToDispatchHandler`, dans le module Delivery, qui
/// créait la course chez le service de dispatch. Ce service est retiré : la plateforme
/// ne livre plus elle-même, le vendeur remet le colis et le clôt avec le code de
/// l'acheteur (`Shipment.MarkDeliveredBySeller`).
///
/// ON CONTINUE POURTANT DE LE PUBLIER. Le projet `hba-delivery` reprendra la logistique
/// une fois terminé, et c'est ICI qu'il se branchera : « la commande est prête à
/// partir » reste un fait vrai du domaine, que Shipping est seul à connaître. Le
/// supprimer maintenant obligerait à le réinventer — avec un autre nom, d'autres
/// champs, et des expéditions d'intervalle que personne n'aurait publiées.
///
/// SON SILENCE EST DONC NORMAL, ET C'EST PRÉCISÉMENT CE QU'IL FAUT ÉCRIRE. Le
/// répartiteur ne dit rien quand la liste des gestionnaires est vide : un événement sans
/// destinataire est marqué traité sans avoir rien produit. Tant qu'il n'y avait qu'un
/// seul consommateur, cette discrétion était un piège — elle aurait masqué sa
/// disparition. Aujourd'hui l'absence est l'état attendu ; demain, l'apparition d'un
/// gestionnaire sera le branchement de `hba-delivery`. Entre les deux, personne ne doit
/// lire ce vide comme une panne.
/// ═══════════════════════════════════════════════════════════════════════════════════
public sealed class PublishOrderReadyForDeliveryHandler : IDomainEventHandler<ShipmentPreparingDomainEvent>
{
    private readonly IShipmentRepository _shipments;
    private readonly IOrderingModuleApi _orders;
    private readonly ISellerModuleApi _sellers;
    private readonly IIdentityModuleApi _identity;
    private readonly IInventoryModuleApi _inventory;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<PublishOrderReadyForDeliveryHandler> _logger;

    public PublishOrderReadyForDeliveryHandler(
        IShipmentRepository shipments,
        IOrderingModuleApi orders,
        ISellerModuleApi sellers,
        IIdentityModuleApi identity,
        IInventoryModuleApi inventory,
        IIntegrationEventPublisher publisher,
        ILogger<PublishOrderReadyForDeliveryHandler> logger)
    {
        _shipments = shipments;
        _orders = orders;
        _sellers = sellers;
        _identity = identity;
        _inventory = inventory;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(
        ShipmentPreparingDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var shipments = await _shipments.ListByOrderAsync(domainEvent.OrderId, cancellationToken);
        if (shipments.Count == 0)
        {
            return;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // L'EXPÉDITION QUI DÉCLENCHE EST CONSIDÉRÉE PRÊTE, SANS RELIRE SON STATUT.
        //
        // Les événements de domaine sont dispatchés AVANT que la transaction ne soit
        // commitée. Selon que la lecture ci-dessus est suivie par EF ou non, elle peut
        // rendre l'ancien statut de cette expédition-là — auquel cas la commande ne
        // serait JAMAIS déclarée prête, puisqu'il manquerait toujours la dernière part.
        //
        // Une panne de ce genre ne se voit pas : rien n'échoue, il ne se passe
        // simplement rien. On ne se fie donc pas à la relecture pour ce qu'on sait déjà.
        // ─────────────────────────────────────────────────────────────────────────
        var pending = shipments
            .Where(s => s.Id.Value != domainEvent.ShipmentId)
            .Where(s => s.Status is ShipmentStatus.Pending)
            .ToList();

        if (pending.Count > 0)
        {
            return;
        }

        // Les expéditions annulées ne se ramassent pas — et ne doivent pas empêcher les
        // autres de partir.
        var active = shipments
            .Where(s => s.Status != ShipmentStatus.Cancelled)
            .OrderBy(s => s.CreatedAtUtc)
            .ToList();

        if (active.Count == 0)
        {
            return;
        }

        var order = await _orders.GetOrderAsync(domainEvent.OrderId, cancellationToken);
        if (order?.ShippingAddress is not { } shipTo)
        {
            // Sans adresse de livraison, aucune course n'est possible. C'est un
            // invariant du checkout : le signaler fort, c'est signaler que le checkout
            // a laissé passer une commande qu'il n'aurait pas dû accepter.
            _logger.LogError(
                "Commande {OrderId} prête à livrer mais SANS adresse exploitable : aucune course ne sera créée.",
                domainEvent.OrderId);
            return;
        }

        var pickups = new List<DeliveryPickupPointContract>(active.Count);
        foreach (var shipment in active)
        {
            pickups.Add(await BuildPickupAsync(shipment, cancellationToken));
        }

        await _publisher.PublishAsync(
            new OrderReadyForDeliveryIntegrationEvent
            {
                OrderId = order.Id,
                ShippingFee = order.ShippingFee,
                Currency = order.Currency,
                Pickups = pickups,
                Dropoff = new DeliveryDropoffContract(
                    order.BuyerId,
                    shipTo.Recipient,
                    shipTo.Phone,
                    shipTo.CommuneCode ?? string.Empty,
                    BeninGeography.CommuneName(shipTo.CommuneCode),
                    shipTo.Quartier,
                    shipTo.Landmark,
                    shipTo.Line1,
                    shipTo.Latitude,
                    shipTo.Longitude)
            },
            cancellationToken);

        _logger.LogInformation(
            "Commande {OrderId} prête à livrer : {Pickups} point(s) de ramassage annoncés au service coursier.",
            order.Id, pickups.Count);
    }

    private async Task<DeliveryPickupPointContract> BuildPickupAsync(
        Shipment shipment, CancellationToken cancellationToken)
    {
        var seller = await _sellers.GetSellerAsync(shipment.SellerId, cancellationToken);

        // Le téléphone du vendeur vit dans Identity, pas dans Sellers : une boutique
        // n'est pas une personne. C'est pourtant lui que le coursier composera devant
        // une porte fermée, donc il fait partie du brief.
        string? sellerPhone = null;
        if (seller is not null)
        {
            var user = await _identity.GetUserAsync(seller.UserId, cancellationToken);
            sellerPhone = user?.PhoneNumber;
        }

        var location = await _inventory.GetLocationAsync(shipment.ShipFromLocationId, cancellationToken);
        if (location is null)
        {
            _logger.LogError(
                "Expédition {ShipmentId} : lieu de ramassage {LocationId} INTROUVABLE — le coursier " +
                "recevra un point sans adresse.",
                shipment.Id.Value, shipment.ShipFromLocationId);
        }

        return new DeliveryPickupPointContract(
            shipment.Id.Value,
            shipment.SellerId,
            string.IsNullOrWhiteSpace(seller?.ShopName) ? "Boutique" : seller!.ShopName,
            sellerPhone,
            shipment.ShipFromLocationId,
            location?.CommuneCode ?? string.Empty,
            location?.CommuneName ?? string.Empty,
            location?.Quartier,
            location?.Landmark,
            location?.Line,
            location?.Latitude,
            location?.Longitude,
            shipment.Items.Select(i => new DeliveryParcelContract(i.Sku, i.Quantity)).ToList());
    }
}
