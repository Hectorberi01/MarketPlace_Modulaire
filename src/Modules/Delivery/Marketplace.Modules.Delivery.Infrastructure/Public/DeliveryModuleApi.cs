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
