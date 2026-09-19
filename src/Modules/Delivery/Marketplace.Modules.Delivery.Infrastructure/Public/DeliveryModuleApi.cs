using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Infrastructure.Dispatch;

namespace Marketplace.Modules.Delivery.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Delivery. Lecture seule :
/// Settlement s'en sert pour savoir où verser l'argent d'un coursier, la console
/// d'administration pour voir la flotte, et rien n'écrit ici depuis l'extérieur.
/// </summary>
internal sealed class DeliveryModuleApi : IDeliveryModuleApi
{
    private readonly ISender _sender;
    private readonly IDispatchServiceClient _dispatch;
    private readonly ILogger<DeliveryModuleApi> _logger;

    public DeliveryModuleApi(
        ISender sender, IDispatchServiceClient dispatch, ILogger<DeliveryModuleApi> logger)
    {
        _sender = sender;
        _dispatch = dispatch;
        _logger = logger;
    }

    public async Task<CourierSummary?> GetCourierAsync(Guid courierId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCourierQuery(courierId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<CourierSummary?> GetCourierByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCourierByUserQuery(userId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Relaie les positions du service de dispatch, ou dit pourquoi il n'a pas répondu.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES QUATRE PANNES SONT DISTINGUÉES, PARCE QU'ELLES N'APPELLENT PAS LE MÊME GESTE.
    ///
    ///   configuration absente ... rien ne marchera jamais, il faut renseigner le déploiement
    ///   délai dépassé ........... le service met trop longtemps, il rame ou il démarre
    ///   appel refusé ou coupé ... service arrêté, réseau, ou signature rejetée
    ///   réponse illisible ....... les deux versions ont divergé
    ///
    /// Un « service injoignable » unique laisserait chercher pendant une heure une panne
    /// réseau là où il manque une variable d'environnement.
    ///
    /// AUCUN MESSAGE NE CITE L'EXCEPTION. Ces phrases partent vers un navigateur : une
    /// trace y exposerait l'adresse interne du service, et parfois plus. Le détail
    /// complet part au journal, avec l'exception, où il est consultable par qui a le
    /// droit de le lire.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<CourierFleetSnapshot> GetFleetAsync(CancellationToken cancellationToken = default)
    {
        // Posé AVANT l'appel : c'est l'instant auquel on a demandé, et c'est le plus
        // ancien des deux. Le poser après ferait passer pour fraîche une réponse qui a
        // mis quatre secondes à venir.
        var observedAt = DateTime.UtcNow;

        try
        {
            var flotte = await _dispatch.GetCourierPositionsAsync(cancellationToken);

            return new CourierFleetSnapshot(
                Reachable: true,
                Failure: null,
                ObservedAtUtc: observedAt,
                OfferMaxAgeSeconds: flotte.OfferMaxAgeSeconds,
                ActionMaxAgeSeconds: flotte.ActionMaxAgeSeconds,
                Couriers: flotte.Couriers
                    .Select(p => new CourierPosition(
                        p.CourierId, p.DisplayName, p.Vehicle, p.Availability,
                        p.Latitude, p.Longitude, p.PositionAtUtc))
                    .ToList());
        }
        catch (DispatchNotConfiguredException ex)
        {
            // Type DÉDIÉ, et non `InvalidOperationException` : `ObjectDisposedException`
            // en dérive, comme plusieurs erreurs d'usage d'EF et de HttpClient, et les
            // capturer ici afficherait à l'écran un « renseignez Dispatch__BaseUrl »
            // impératif et faux.
            return Injoignable(
                observedAt,
                "Le service de dispatch n'est pas configuré sur cette instance. "
                + "Renseignez Dispatch__BaseUrl et Dispatch__OutboundSecret.",
                ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Le budget de lecture a expiré. La condition est essentielle : sans elle,
            // un administrateur qui ferme sa page produirait un « le service met trop
            // longtemps » mensonger, et l'annulation serait avalée au lieu de remonter.
            return Injoignable(
                observedAt,
                "Le service de dispatch n'a pas répondu dans le délai imparti.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            return Injoignable(
                observedAt,
                "Le service de dispatch est injoignable ou a refusé l'appel.",
                ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // `NotSupportedException` AUTANT QUE `JsonException` : `ReadFromJsonAsync`
            // lève la première, et non la seconde, quand le type de contenu reçu n'est
            // pas du JSON — un 200 en text/html d'un proxy intermédiaire, typiquement.
            // Sans elle, la panne la plus banale d'une chaîne de reverse-proxy remontait
            // non capturée, rendait un 500, et emportait avec elle les points de vente
            // que cet écran affiche par ailleurs très bien.
            return Injoignable(
                observedAt,
                "Le service de dispatch a répondu dans une forme inattendue.",
                ex);
        }
    }

    /// <summary>
    /// Relaie le suivi d'une commande, ou dit pourquoi il n'a pas pu être demandé.
    ///
    /// LES MÊMES QUATRE PANNES QUE <see cref="GetFleetAsync"/>, distinguées pour les
    /// mêmes raisons — voir le commentaire là-haut, qui vaut mot pour mot ici.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA CINQUIÈME RÉPONSE N'EST PAS UNE PANNE : « CETTE COMMANDE N'A PAS DE COURSE ».
    ///
    /// Le client rend `null` sur le 404 du dispatch, et ce cas ressort ici en
    /// <c>ByCourier: false</c> avec <c>Reachable: true</c>. C'est la réponse la plus
    /// fréquente de cette méthode — toute commande partie par transporteur, et toute
    /// commande pas encore prête. La confondre avec une panne afficherait un message
    /// d'incident sur des colis qui voyagent très bien.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<OrderDeliveryTracking> GetOrderTrackingAsync(
        Guid orderId, Guid buyerId, CancellationToken cancellationToken = default)
    {
        // Posé AVANT l'appel, comme pour la flotte : c'est l'instant auquel on a demandé,
        // et c'est le plus ancien des deux.
        var observedAt = DateTime.UtcNow;

        try
        {
            var suivi = await _dispatch.GetOrderTrackingAsync(orderId, buyerId, cancellationToken);

            if (suivi is null)
            {
                return SansCourse(observedAt);
            }

            return new OrderDeliveryTracking(
                Reachable: true,
                Failure: null,
                ObservedAtUtc: observedAt,
                ByCourier: true,
                Status: suivi.Status,
                Leg: suivi.Leg,
                PickupsDone: suivi.PickupsDone,
                PickupsTotal: suivi.PickupsTotal,
                DropoffLatitude: suivi.DropoffLatitude,
                DropoffLongitude: suivi.DropoffLongitude,
                AcceptedAtUtc: suivi.AcceptedAtUtc,
                CompletedAtUtc: suivi.CompletedAtUtc,
                PositionMaxAgeSeconds: suivi.PositionMaxAgeSeconds,
                // L'identifiant du coursier et sa disponibilité s'arrêtent ici : le type
                // du dispatch ne les porte pas, et le contrat public non plus.
                Courier: suivi.Courier is null
                    ? null
                    : new TrackedCourierPosition(
                        suivi.Courier.DisplayName,
                        suivi.Courier.Vehicle,
                        suivi.Courier.Latitude,
                        suivi.Courier.Longitude,
                        suivi.Courier.PositionAtUtc));
        }
        catch (DispatchNotConfiguredException ex)
        {
            return SuiviInjoignable(
                observedAt,
                "Le service de livraison n'est pas configuré sur cette instance.",
                ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // La condition est essentielle, pour la raison donnée dans GetFleetAsync :
            // sans elle, un acheteur qui ferme sa page produirait un « délai dépassé »
            // mensonger, et son annulation serait avalée au lieu de remonter.
            return SuiviInjoignable(
                observedAt,
                "Le suivi de livraison n'a pas répondu dans le délai imparti.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            return SuiviInjoignable(
                observedAt,
                "Le suivi de livraison est momentanément indisponible.",
                ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return SuiviInjoignable(
                observedAt,
                "Le suivi de livraison a répondu dans une forme inattendue.",
                ex);
        }
    }

    /// <summary>
    /// Pas de course pour cette commande. Joignable, rien à montrer, aucune erreur.
    /// </summary>
    private static OrderDeliveryTracking SansCourse(DateTime observedAt)
        => new(
            Reachable: true,
            Failure: null,
            ObservedAtUtc: observedAt,
            ByCourier: false,
            Status: null,
            Leg: null,
            PickupsDone: 0,
            PickupsTotal: 0,
            DropoffLatitude: null,
            DropoffLongitude: null,
            AcceptedAtUtc: null,
            CompletedAtUtc: null,
            PositionMaxAgeSeconds: 0,
            Courier: null);

    private OrderDeliveryTracking SuiviInjoignable(DateTime observedAt, string message, Exception cause)
    {
        _logger.LogWarning(cause, "Suivi de livraison impossible : {Message}", message);

        // `ByCourier` à VRAI, et c'est le point délicat de cette méthode : on ne sait pas
        // si cette commande a une course — c'est précisément ce qu'on n'a pas pu
        // demander. Le dire à faux ferait afficher « pas de livraison par coursier », une
        // affirmation tranquille et fausse. À vrai, avec `Reachable` à faux, l'écran sait
        // qu'il ne sait pas, et le dit.
        return new OrderDeliveryTracking(
            Reachable: false,
            Failure: message,
            ObservedAtUtc: observedAt,
            ByCourier: true,
            Status: null,
            Leg: null,
            PickupsDone: 0,
            PickupsTotal: 0,
            DropoffLatitude: null,
            DropoffLongitude: null,
            AcceptedAtUtc: null,
            CompletedAtUtc: null,
            // Zéro, et non une valeur plausible : on ne le connaît pas, et une valeur
            // inventée serait indiscernable d'une valeur reçue.
            PositionMaxAgeSeconds: 0,
            Courier: null);
    }

    private CourierFleetSnapshot Injoignable(DateTime observedAt, string message, Exception cause)
    {
        _logger.LogWarning(cause, "Relevé de la flotte impossible : {Message}", message);
        // Les seuils à zéro, et non à une valeur plausible : on ne les connaît pas, et
        // une valeur inventée serait indiscernable d'une valeur reçue.
        return new CourierFleetSnapshot(
            Reachable: false,
            Failure: message,
            ObservedAtUtc: observedAt,
            OfferMaxAgeSeconds: 0,
            ActionMaxAgeSeconds: 0,
            Couriers: []);
    }
}
