using System.Text.Json;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Api.Notifications;

/// <summary>
/// Fait sonner le téléphone du coursier, en demandant à la marketplace de pousser.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE SERVICE N'ENVOIE PAS LUI-MÊME LA NOTIFICATION.
///
/// Les jetons d'appareil, les préférences de notification et le compte de service FCM
/// vivent dans la marketplace. Le compose de production va jusqu'à ANNULER le montage du
/// compte de service pour ce conteneur — « ce service n'envoie aucune notification
/// push » est une décision écrite, pas un oubli.
///
/// On la respecte plutôt que de la contourner : LE DISPATCH ANNONCE, LA MARKETPLACE
/// POUSSE. Dupliquer ici les jetons d'appareil créerait une seconde copie à tenir à jour
/// d'un état qui change à chaque réinstallation d'application.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ENVOI EST DÉTACHÉ, ET C'EST LA PROPRIÉTÉ LA PLUS IMPORTANTE DE CE FICHIER.
///
/// DeliveryJobService notifie les candidats EN SÉRIE, dans une boucle qu'il attend. Avec
/// le hub SignalR cela ne coûte rien. Un aller-retour HTTP par candidat, lui, se paie :
/// cinq coursiers devant une marketplace lente, et la fenêtre d'offre — vingt-cinq
/// secondes — est consommée par la boucle qui annonce l'offre.
///
/// NotifyOfferAsync rend donc la main TOUT DE SUITE et laisse l'envoi partir seul. Deux
/// conséquences tenues ici, et non ailleurs :
///   • le travail détaché ouvre SA PROPRE PORTÉE d'injection. Emporter les services de
///     la portée appelante les ferait utiliser après sa fermeture — une panne qui
///     n'apparaît qu'en charge, et jamais en développement ;
///   • il n'emporte PAS le jeton d'annulation de l'appelant, qui meurt avec la requête,
///     mais une échéance à lui, liée à l'arrêt de l'application.
///
/// Rien ne remonte : la méthode ne peut pas échouer du point de vue de l'appelant. C'est
/// exact et c'est voulu — l'offre est déjà enregistrée, et un push manqué se rattrape à
/// l'ouverture de l'application.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PushOfferNotifier : IOfferNotifier
{
    /// <summary>
    /// Web : camelCase, comme la file sortante et comme la lecture côté marketplace.
    /// Les trois doivent s'accorder, sinon les champs arrivent à null sans erreur.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Plus longue que le délai du client HTTP (dix secondes) : c'est un garde-fou de
    /// dernier recours contre un travail détaché qui ne se terminerait jamais, pas le
    /// délai d'attente réel.
    /// </summary>
    private static readonly TimeSpan Echeance = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _portees;
    private readonly IHostApplicationLifetime _cycleDeVie;
    private readonly ILogger<PushOfferNotifier> _logger;

    public PushOfferNotifier(
        IServiceScopeFactory portees,
        IHostApplicationLifetime cycleDeVie,
        ILogger<PushOfferNotifier> logger)
    {
        _portees = portees;
        _cycleDeVie = cycleDeVie;
        _logger = logger;
    }

    public Task NotifyOfferAsync(
        CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        // Sérialisé ICI, dans le fil appelant, pendant que la course est encore sous la
        // main : le travail détaché ne touche plus à l'agrégat, qui appartient à un
        // DbContext sur le point d'être fermé.
        var charge = JsonSerializer.Serialize(
            new CourierOfferPayload(courierId.Value, job.Id.Value), JsonOptions);

        _ = Task.Run(() => EnvoyerAsync(courierId, job.Id, charge), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// RIEN. Le retrait d'offre ne se pousse pas, et l'absence est raisonnée.
    ///
    /// Une notification déjà affichée sur un écran verrouillé ne se retire pas de façon
    /// fiable, et en envoyer une seconde pour dire « finalement non » sonnerait le
    /// téléphone une deuxième fois pour annoncer qu'il ne se passe rien. Le hub, lui,
    /// retire bien l'offre de l'écran d'une application ouverte : c'est son rôle.
    ///
    /// Le coursier qui ouvre une notification périmée reçoit un 409 correct, et l'écran
    /// se recharge sur les offres réellement en cours.
    /// </summary>
    public Task NotifyOfferWithdrawnAsync(
        CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task EnvoyerAsync(CourierId courierId, DeliveryJobId jobId, string charge)
    {
        try
        {
            using var annulation = CancellationTokenSource.CreateLinkedTokenSource(
                _cycleDeVie.ApplicationStopping);
            annulation.CancelAfter(Echeance);

            using var portee = _portees.CreateScope();
            var marketplace = portee.ServiceProvider.GetRequiredService<IMarketplaceClient>();

            await marketplace.SendCourierOfferAsync(charge, annulation.Token);
        }
        catch (Exception ex)
        {
            // AUCUNE EXCEPTION NE SORT D'ICI. Un travail détaché qui lève n'a personne
            // pour l'observer : selon la configuration, le processus peut tomber.
            _logger.LogWarning(
                ex,
                "Course {JobId} : notification push non envoyée au coursier {CourierId} — "
                + "l'offre reste valable, il la verra en ouvrant l'application.",
                jobId, courierId);
        }
    }
}
