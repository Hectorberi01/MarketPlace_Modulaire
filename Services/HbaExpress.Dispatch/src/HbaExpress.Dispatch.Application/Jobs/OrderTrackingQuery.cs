using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Le coursier tel que l'ACHETEUR a le droit de le voir.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE QUI N'EST PAS DANS CE TYPE EST AUSSI IMPORTANT QUE CE QUI Y EST.
///
/// <see cref="FleetCourierView"/> existe déjà et porte les mêmes coordonnées : on aurait
/// pu le réutiliser. Il porte aussi l'IDENTIFIANT du coursier et sa DISPONIBILITÉ, deux
/// choses qui regardent la supervision et pas un client. L'identifiant permettrait de
/// suivre un même coursier d'une commande à l'autre ; la disponibilité dirait à
/// l'acheteur si l'homme qui lui apporte son colis a d'autres courses en attente.
///
/// Un type distinct coûte quinze lignes et garantit qu'un champ ajouté à la vue de
/// supervision ne part pas, par héritage silencieux, vers des milliers de navigateurs.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="DisplayName">Nom affiché, tel que la marketplace l'a annoncé.</param>
/// <param name="Vehicle">« Motorcycle », « Bicycle », « Car »… — l'acheteur guette une moto.</param>
/// <param name="Latitude">Degrés décimaux, WGS 84.</param>
/// <param name="Longitude">Degrés décimaux, WGS 84.</param>
/// <param name="PositionAtUtc">
/// QUAND cette position a été relevée. L'acheteur DOIT le voir : un repère sur une carte
/// a la même allure qu'il date de dix secondes ou d'une heure, et un coursier entré dans
/// une zone sans réseau continuerait sinon d'« avancer » là où il a été vu pour la
/// dernière fois.
/// </param>
public sealed record TrackedCourierView(
    string DisplayName,
    string Vehicle,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);

/// <summary>
/// Où en est la livraison d'une commande, pour celui qui l'attend.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// POURQUOI « LEG » EXISTE, ET POURQUOI S'EN PASSER RENDAIT LA CARTE INQUIÉTANTE.
///
/// Entre l'acceptation et le ramassage, le coursier roule VERS LA BOUTIQUE — souvent à
/// l'opposé de l'acheteur. Sans cette étape nommée, le seul rendu honnête serait un point
/// qui s'éloigne, et l'acheteur lit « mon colis part dans la mauvaise direction ». Avec
/// elle, l'écran écrit « en route vers la boutique », puis « en route vers vous », et le
/// même mouvement devient une information au lieu d'une inquiétude.
/// ═════════════════════════════════════════════════════════════════════════════════
///
/// LES COORDONNÉES DES POINTS DE RAMASSAGE NE SORTENT PAS D'ICI. Seul le COMPTE des
/// points traités sort. L'adresse exacte d'un vendeur est son affaire, et la course n'est
/// pas une raison de la publier à ses acheteurs.
/// </summary>
/// <param name="Status">Statut de la course, tel quel : « Accepted », « InTransit »…</param>
/// <param name="Leg">
/// « ToPickup », « ToBuyer », ou « None » quand rien n'est en cours. Déduit du statut, et
/// déduit ICI : le calculer dans chaque application garantirait que web et mobile
/// divergent le jour où un statut s'ajoute.
/// </param>
/// <param name="PickupsDone">Points de ramassage TRAITÉS — ramassés, ou constatés impossibles.</param>
/// <param name="PickupsTotal">Nombre de boutiques à visiter pour cette commande.</param>
/// <param name="DropoffLatitude">Destination, quand la commande en porte une. Sinon nul.</param>
/// <param name="PositionMaxAgeSeconds">
/// Âge au-delà duquel CE SERVICE lui-même ne laisse plus le coursier agir sur sa course.
/// Il voyage avec la réponse pour que l'écran cesse d'affirmer une présence que le
/// serveur ne croit déjà plus — et pour qu'il n'y ait pas deux valeurs à garder d'accord.
/// </param>
/// <param name="Courier">Nul tant que personne n'a pris la course, ou une fois livrée.</param>
public sealed record OrderTrackingView(
    Guid OrderId,
    Guid JobId,
    string Status,
    string Leg,
    int PickupsDone,
    int PickupsTotal,
    double? DropoffLatitude,
    double? DropoffLongitude,
    DateTime? AcceptedAtUtc,
    DateTime? CompletedAtUtc,
    int PositionMaxAgeSeconds,
    TrackedCourierView? Courier);

/// <summary>
/// Lecture du suivi d'UNE commande, pour l'acheteur qui l'attend.
///
/// Vit à part de <see cref="DeliveryJobService"/> et de <see cref="JobProgressService"/>,
/// qui MUTENT, pour la même raison que <see cref="FleetQuery"/> : rien de ce qui répond à
/// « où en est ma commande » ne doit pouvoir déplacer une course.
/// </summary>
public sealed class OrderTrackingQuery
{
    private readonly IDeliveryJobRepository _jobs;
    private readonly ICourierRepository _couriers;

    public OrderTrackingQuery(IDeliveryJobRepository jobs, ICourierRepository couriers)
    {
        _jobs = jobs;
        _couriers = couriers;
    }

    /// <summary>
    /// Le suivi, ou <c>null</c> quand il n'y a rien à suivre.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// NUL DANS DEUX CAS, ET L'APPELANT N'A PAS À LES DISTINGUER.
    ///
    ///   aucune course ....... la commande part par transporteur, ou n'est pas encore
    ///                         prête. Il n'y a pas d'erreur, il n'y a rien à montrer.
    ///   acheteur différent .. la commande existe, mais pas pour ce demandeur.
    ///
    /// Les confondre est DÉLIBÉRÉ. Répondre « cette commande existe mais n'est pas à
    /// vous » ferait de cette route un oracle d'existence de commandes : on apprendrait,
    /// un identifiant après l'autre, lesquels sont réels. Le BFF a déjà vérifié la
    /// possession de son côté ; ce contrôle-ci est la seconde serrure, et une seconde
    /// serrure qui annonce ce qu'elle protège n'en est pas une.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="orderId">La commande dont on veut le suivi.</param>
    /// <param name="buyerId">
    /// L'acheteur qui demande. COMPARÉ, pas seulement journalisé : c'est ce qui empêche
    /// un appel interne légitime, rejoué avec un autre identifiant de commande, de rendre
    /// la position d'un coursier et l'adresse d'un inconnu.
    /// </param>
    public async Task<OrderTrackingView?> GetAsync(
        Guid orderId, Guid buyerId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByOrderAsync(orderId, cancellationToken);
        if (job is null || job.Dropoff.BuyerId != buyerId)
        {
            return null;
        }

        var leg = job.Status switch
        {
            DeliveryJobStatus.Accepted or DeliveryJobStatus.PickingUp => "ToPickup",
            DeliveryJobStatus.InTransit => "ToBuyer",
            _ => "None",
        };

        return new OrderTrackingView(
            OrderId: job.OrderId,
            JobId: job.Id.Value,
            Status: job.Status.ToString(),
            Leg: leg,
            // « Traités », et non « ramassés » : un vendeur absent laisse son point non
            // ramassé pour toujours, et compter les ramassages ferait stagner la
            // progression sur un point que le coursier a pourtant cessé d'attendre.
            PickupsDone: job.Pickups.Count(p => p.IsResolved),
            PickupsTotal: job.Pickups.Count,
            DropoffLatitude: job.Dropoff.Position?.Latitude,
            DropoffLongitude: job.Dropoff.Position?.Longitude,
            AcceptedAtUtc: job.AcceptedAtUtc,
            CompletedAtUtc: job.CompletedAtUtc,
            PositionMaxAgeSeconds: (int)CourierActionPolicy.PositionMaxAge.TotalSeconds,
            Courier: await CourierAsync(job, leg, cancellationToken));
    }

    /// <summary>
    /// Le coursier, UNIQUEMENT pendant qu'il porte la course.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA POSITION S'ARRÊTE AVEC LA COURSE, ET CE N'EST PAS UNE ÉCONOMIE D'AFFICHAGE.
    ///
    /// `leg` vaut « None » dès que la course est livrée, échouée ou annulée. Continuer à
    /// rendre la position après la livraison laisserait un acheteur suivre à la trace
    /// l'homme qui vient de sonner chez lui — vers sa course suivante, et vers chez lui.
    /// Le suivi d'un colis est une raison de savoir où est un coursier ; l'avoir eue une
    /// fois n'en est pas une de le garder.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<TrackedCourierView?> CourierAsync(
        DeliveryJob job, string leg, CancellationToken cancellationToken)
    {
        if (leg == "None" || job.AssignedCourierId is not { } courierId)
        {
            return null;
        }

        var courier = await _couriers.GetByIdAsync(courierId, cancellationToken);

        // Copiées dans des locaux avant le contrôle, comme ailleurs dans ce service :
        // l'analyse de nullabilité suit un local sans réserve, là où elle peut
        // réinitialiser l'état d'une propriété au premier appel qui la sépare de son test.
        var position = courier?.LastPosition;
        var releveA = courier?.LastPositionAtUtc;

        // Position absente : le coursier a accepté puis n'a plus rien relevé. L'écran
        // dira l'étape sans dessiner de repère, ce qui vaut mieux qu'un point inventé.
        if (courier is null || position is null || releveA is null)
        {
            return null;
        }

        return new TrackedCourierView(
            courier.DisplayName,
            courier.Vehicle.ToString(),
            position.Latitude,
            position.Longitude,
            releveA.Value);
    }
}
