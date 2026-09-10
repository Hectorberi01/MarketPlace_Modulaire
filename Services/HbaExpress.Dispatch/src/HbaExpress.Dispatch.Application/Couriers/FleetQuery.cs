using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Matching;

namespace HbaExpress.Dispatch.Application.Couriers;

/// <summary>
/// Un coursier tel qu'une console de supervision a besoin de le voir : où il est, dans
/// quel état, et DEPUIS QUAND on le sait.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// L'ÂGE DE LA POSITION EN FAIT PARTIE, ET CE N'EST PAS UN DÉTAIL D'AFFICHAGE.
///
/// Une position s'écrase toutes les quinze secondes. Passé un certain âge, le service
/// cesse lui-même de croire le coursier là où il dit être — et il le fait à DEUX
/// niveaux, pas un (voir <see cref="FleetView"/>). Un repère sur une carte ne dit rien
/// de cela : il a exactement la même allure qu'il date de dix secondes ou de deux heures.
///
/// `PositionAtUtc` voyage donc avec la position, et l'appelant DOIT le montrer. Sans lui,
/// la carte affirme une présence sur le terrain que le serveur, lui, ne croit plus.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record FleetCourierView(
    Guid CourierId,
    string DisplayName,
    string Vehicle,
    // « Online » ou « OnDelivery ». Les hors ligne ne sont jamais renvoyés — la requête
    // les écarte, l'appelant n'a donc pas de troisième cas à traiter.
    string Availability,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);

/// <summary>
/// La flotte, ET LES DEUX SEUILS QUI DISENT COMMENT LA LIRE.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// POURQUOI LES SEUILS VOYAGENT AVEC LES DONNÉES PLUTÔT QUE D'ÊTRE RECOPIÉS EN FACE.
///
/// Une position vieillit en deux temps dans ce service, et les deux comptent pour qui
/// supervise :
///
///   au-delà de OfferMaxAge ..... le coursier ne reçoit PLUS D'OFFRE. `CourierMatcher`
///                                l'écarte de chaque vague, y compris élargie —
///                                `DispatchPolicy.Widen` ne touche pas à ce seuil.
///   au-delà de ActionMaxAge .... il ne peut plus rien faire de la course qu'il a déjà :
///                                ni ramasser, ni partir, ni livrer.
///
/// Les deux valeurs sont différentes À DESSEIN — deux minutes pour offrir, parce qu'on
/// mesure une distance, cinq pour agir, parce qu'une coupure réseau sous un pont ne doit
/// pas empêcher de valider un ramassage. Les recopier dans une console d'administration
/// garantirait qu'elles divergent le jour où l'une d'elles bouge ici, et la carte
/// afficherait alors comme disponibles des coursiers auxquels ce service ne propose déjà
/// plus rien. Elles partent donc AVEC la réponse.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="OfferMaxAgeSeconds">Âge au-delà duquel le coursier ne reçoit plus d'offre.</param>
/// <param name="ActionMaxAgeSeconds">Âge au-delà duquel il ne peut plus agir sur une course.</param>
/// <param name="Couriers">Les coursiers sur le terrain, ordonnés par nom.</param>
public sealed record FleetView(
    int OfferMaxAgeSeconds,
    int ActionMaxAgeSeconds,
    IReadOnlyList<FleetCourierView> Couriers);

/// <summary>
/// Lecture seule de la flotte présente. Vit à part de <c>CourierService</c>, qui MUTE :
/// mélanger une lecture de supervision aux commandes du coursier aurait fait grossir un
/// service déjà chargé, et brouillé ce qui a le droit d'écrire.
/// </summary>
public sealed class FleetQuery
{
    private readonly ICourierRepository _couriers;

    public FleetQuery(ICourierRepository couriers) => _couriers = couriers;

    public async Task<FleetView> ListOnFieldAsync(CancellationToken cancellationToken = default)
    {
        var couriers = await _couriers.ListOnFieldAsync(cancellationToken);

        var vues = couriers
            // `LastPositionAtUtc` est déjà non nul par la requête, et `LastPosition` avec
            // lui — `UpdatePosition` écrit les deux ensemble. Ce test ne filtre donc rien :
            // il donne son certificat à l'analyse de nullabilité, qui ne peut pas savoir
            // ce que le `Where` de la requête a garanti.
            .Where(c => c.LastPosition is not null && c.LastPositionAtUtc is not null)
            .Select(c => new FleetCourierView(
                c.Id.Value,
                c.DisplayName,
                c.Vehicle.ToString(),
                c.Availability.ToString(),
                c.LastPosition!.Latitude,
                c.LastPosition!.Longitude,
                c.LastPositionAtUtc!.Value))
            .ToList();

        // Lus sur les politiques elles-mêmes, jamais réécrits en dur : c'est ce qui fait
        // que le jour où l'une bouge, l'écran d'en face suit sans qu'on y pense.
        return new FleetView(
            OfferMaxAgeSeconds: (int)DispatchPolicy.FirstRound.PositionMaxAge.TotalSeconds,
            ActionMaxAgeSeconds: (int)CourierActionPolicy.PositionMaxAge.TotalSeconds,
            Couriers: vues);
    }
}
