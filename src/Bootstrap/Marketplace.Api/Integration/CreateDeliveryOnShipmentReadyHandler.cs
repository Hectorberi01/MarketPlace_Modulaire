using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Pricing.Queries;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using MediatR;

namespace Marketplace.Api.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE RACCORDEMENT MARKETPLACE → HBA DELIVERY.
///
/// POURQUOI CE FICHIER EST DANS Marketplace.Api ET NON DANS UN MODULE
///
/// Il connaît Shipping, Ordering, Inventory, Sellers ET Deliveries. Aucun module
/// n'a le droit d'en savoir autant :
///
///   • le mettre dans Deliveries violerait la règle qui interdit à ce module de
///     dépendre de quoi que ce soit d'autre — la règle qui le rend extractible et
///     vendable à des marchands tiers ;
///   • le mettre dans Shipping obligerait Shipping à connaître HBA Delivery, donc
///     à en dépendre le jour où les colis partiraient par un transporteur externe.
///
/// Le composition root, lui, a déjà le droit de tout connaître : c'est sa
/// définition. Le couplage existe — il est inévitable, il faut bien que quelqu'un
/// relie les deux — mais il est ISOLÉ ici, dans un fichier qu'on supprime pour
/// détacher les deux mondes.
///
/// ⚠️ CE QUE CE HANDLER NE FAIT PAS : DEVINER.
///
/// Une course a besoin de deux positions, deux repères et deux téléphones. Quand
/// une donnée manque, il REFUSE et le dit. Il ne prend pas le centroïde de la
/// commune — plusieurs kilomètres d'erreur dans le grand Cotonou, donc un prix
/// faux et un livreur envoyé au mauvais endroit. Il ne met pas de téléphone
/// bidon. Un colis non enlevé se voit ; un colis livré à la mauvaise adresse se
/// paie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CreateDeliveryOnShipmentReadyHandler
    : IIntegrationEventHandler<ShipmentReadyForPickupIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IOrderingModuleApi _ordering;
    private readonly IInventoryModuleApi _inventory;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<CreateDeliveryOnShipmentReadyHandler> _logger;

    public CreateDeliveryOnShipmentReadyHandler(
        ISender sender,
        IOrderingModuleApi ordering,
        IInventoryModuleApi inventory,
        ISellerModuleApi sellers,
        ILogger<CreateDeliveryOnShipmentReadyHandler> logger)
    {
        _sender = sender;
        _ordering = ordering;
        _inventory = inventory;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(
        ShipmentReadyForPickupIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var order = await _ordering.GetOrderAsync(integrationEvent.OrderId, cancellationToken);
        var location = await _inventory.GetLocationAsync(integrationEvent.ShipFromLocationId, cancellationToken);
        var seller = await _sellers.GetSellerAsync(integrationEvent.SellerId, cancellationToken);

        var pickup = BuildPickup(location, seller);
        var dropoff = BuildDropoff(order?.ShippingAddress);

        // ─────────────────────────────────────────────────────────────────────
        // LE REFUS EST BRUYANT, ET IL NOMME CE QUI MANQUE.
        //
        // « Impossible de créer la course » sans plus de précision obligerait
        // l'exploitation à ouvrir la base pour comprendre. La liste des champs
        // absents est ce qui transforme un incident en tâche : « la boutique X
        // n'a pas de position, allez la relever ».
        //
        // On journalise en Warning et non en Error : ce n'est pas une panne, c'est
        // une donnée incomplète. Un Error par colis noierait les vraies pannes.
        // ─────────────────────────────────────────────────────────────────────
        var missing = pickup.Missing.Concat(dropoff.Missing).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "HBA Delivery : course NON créée pour l'expédition {ShipmentId} (commande {OrderId}, "
                + "vendeur {SellerId}). Données manquantes : {Missing}. Le colis est prêt mais aucun "
                + "livreur ne sera cherché tant que ces champs ne sont pas renseignés.",
                integrationEvent.ShipmentId, integrationEvent.OrderId, integrationEvent.SellerId,
                string.Join(", ", missing));

            return;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ON DEMANDE UN DEVIS AVANT DE CRÉER LA COURSE.
        //
        // Sans lui, Delivery.Price reste nul — et une course sans prix n'est
        // facturable par personne ET ne produit AUCUN gain pour le livreur, qui a
        // pourtant roulé. La première version de ce fichier créait exactement
        // cela : des courses gratuites, en silence.
        //
        // ⚠️ CE PRIX N'EST PAS CELUI PAYÉ PAR L'ACHETEUR.
        //
        // L'acheteur a réglé « ShippingFee », calculé par le module Shipping au
        // moment du panier. Le prix calculé ici est le COÛT RÉEL de la course,
        // qui sert à rémunérer le livreur. Les deux n'ont aucune raison de
        // coïncider, et l'écart est la marge — ou la perte — de HBA sur la
        // livraison. Personne ne le mesure aujourd'hui : les faire converger, ou
        // au moins suivre l'écart, est une décision produit qui reste à prendre.
        // ─────────────────────────────────────────────────────────────────────
        var quote = await _sender.Send(
            new GetQuoteQuery(
                pickup.Stop!.Latitude!.Value, pickup.Stop.Longitude!.Value,
                dropoff.Stop!.Latitude!.Value, dropoff.Stop.Longitude!.Value,
                WeightKg: 0m,
                Size: PackageSize.Small,
                DeliveryType: DeliveryType.Standard,
                // Véhicule non imposé : le moteur retient le plus petit capable,
                // donc le moins cher.
                VehicleType: null),
            cancellationToken);

        if (quote.IsFailure)
        {
            _logger.LogError(
                "HBA Delivery : course NON créée pour l'expédition {ShipmentId} — le devis a échoué "
                + "({Code} : {Message}). Vérifiez qu'une grille tarifaire couvre ce trajet.",
                integrationEvent.ShipmentId, quote.Error.Code, quote.Error.Message);

            return;
        }

        var command = new CreateDeliveryCommand(
            // ─────────────────────────────────────────────────────────────────
            // LA RÉFÉRENCE EST CELLE DE L'EXPÉDITION, PAS DE LA COMMANDE.
            //
            // Une commande multi-vendeur produit PLUSIEURS expéditions, donc
            // plusieurs courses. Prendre l'identifiant de commande ferait tomber
            // toutes les expéditions d'une même commande sur la même clé
            // d'idempotence : la première créerait la course, les autres
            // recevraient silencieusement son identifiant, et les colis des
            // autres vendeurs ne partiraient jamais.
            //
            // Le format est celui de ShipmentReference, partagé avec les handlers
            // de retour : c'est le SEUL fil entre les deux mondes, et deux façons
            // de l'écrire finiraient par diverger.
            // ─────────────────────────────────────────────────────────────────
            Reference: ShipmentReference.For(integrationEvent.ShipmentId),
            Source: DeliverySource.HbaExpress,
            Type: DeliveryType.Standard,
            Pickup: pickup.Stop!,
            Dropoff: dropoff.Stop!,
            Package: new DeliveryPackageInput(
                Description: DescribePackage(order!),
                // Le poids n'est pas connu : la marketplace ne le collecte nulle
                // part. Nul, il est présumé transportable à moto — ce qui est vrai
                // pour l'écrasante majorité des colis, et faux pour les autres.
                // Collecter un poids au catalogue est la vraie correction.
                WeightKg: null),
            QuoteId: quote.Value.QuoteId);

        var result = await _sender.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError(
                "HBA Delivery : création de course REFUSÉE pour l'expédition {ShipmentId} — {Code} : {Message}",
                integrationEvent.ShipmentId, result.Error.Code, result.Error.Message);

            return;
        }

        _logger.LogInformation(
            "HBA Delivery : course {DeliveryId} créée pour l'expédition {ShipmentId} (commande {OrderId}).",
            result.Value, integrationEvent.ShipmentId, integrationEvent.OrderId);
    }

    /// <summary>Un point construit, ou la liste de ce qui l'a empêché.</summary>
    private readonly record struct StopAttempt(DeliveryStopInput? Stop, IReadOnlyList<string> Missing);

    private static StopAttempt BuildPickup(FulfillmentLocationSummary? location, SellerSummary? seller)
    {
        var missing = new List<string>();

        if (location is null)
        {
            return new StopAttempt(null, ["lieu d'expédition introuvable"]);
        }

        if (string.IsNullOrWhiteSpace(location.Landmark))
        {
            missing.Add("repère du lieu d'expédition");
        }

        if (location.Latitude is null || location.Longitude is null)
        {
            missing.Add("position GPS du lieu d'expédition");
        }

        // ─────────────────────────────────────────────────────────────────────
        // LE NUMÉRO VIENT DU LIEU, PAS DU VENDEUR.
        //
        // La première version prenait le téléphone déclaré dans le dossier KYB :
        // un champ facultatif, celui du gérant, jamais conçu pour la logistique.
        // Un vendeur avec trois boutiques donnait le même numéro pour les trois,
        // et un livreur perdu devant la mauvaise porte appelait quelqu'un qui n'y
        // était pas.
        //
        // Address.ContactPhone existe désormais et est obligatoire à la saisie.
        // Il reste nullable ici : les lieux enregistrés AVANT cette règle n'en ont
        // pas, et EF les matérialise sans repasser par la validation.
        // ─────────────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(location.ContactPhone))
        {
            missing.Add("téléphone du lieu d'expédition");
        }

        if (missing.Count > 0)
        {
            return new StopAttempt(null, missing);
        }

        return new StopAttempt(
            new DeliveryStopInput(
                ContactName: seller?.ShopName ?? "Point de retrait",
                Phone: location.ContactPhone,
                Commune: location.CommuneCode,
                Quartier: location.Quartier,
                Landmark: location.Landmark,
                Instructions: location.Line,
                Latitude: location.Latitude,
                Longitude: location.Longitude),
            []);
    }

    private static StopAttempt BuildDropoff(OrderShippingAddressSummary? address)
    {
        if (address is null)
        {
            return new StopAttempt(null, ["adresse de livraison de la commande"]);
        }

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(address.CommuneCode))
        {
            missing.Add("commune de livraison");
        }

        if (string.IsNullOrWhiteSpace(address.Landmark))
        {
            missing.Add("repère de livraison");
        }

        if (string.IsNullOrWhiteSpace(address.Phone))
        {
            missing.Add("téléphone du destinataire");
        }

        if (address.Latitude is null || address.Longitude is null)
        {
            missing.Add("position GPS de l'adresse de livraison");
        }

        if (missing.Count > 0)
        {
            return new StopAttempt(null, missing);
        }

        return new StopAttempt(
            new DeliveryStopInput(
                ContactName: address.Recipient,
                Phone: address.Phone,
                Commune: address.CommuneCode,
                Quartier: address.Quartier,
                Landmark: address.Landmark,
                Instructions: null,
                Latitude: address.Latitude,
                Longitude: address.Longitude),
            []);
    }

    /// <summary>
    /// Ce que le livreur lit. Volontairement NON DÉTAILLÉ : le moteur logistique
    /// ignore la nature commerciale de ce qu'il transporte, et un livreur n'a pas
    /// à savoir que le colis contient un téléphone à 300 000 francs.
    /// </summary>
    private static string DescribePackage(OrderSummary order)
    {
        var articles = order.Lines.Sum(l => l.Quantity);

        return articles <= 1 ? "Colis marchand (1 article)" : $"Colis marchand ({articles} articles)";
    }
}
