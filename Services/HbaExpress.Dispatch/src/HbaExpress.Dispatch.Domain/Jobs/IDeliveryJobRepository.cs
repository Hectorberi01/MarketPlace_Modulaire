using HbaExpress.Dispatch.Domain.Couriers;

namespace HbaExpress.Dispatch.Domain.Jobs;

public interface IDeliveryJobRepository
{
    Task AddAsync(DeliveryJob job, CancellationToken cancellationToken = default);

    Task<DeliveryJob?> GetByIdAsync(DeliveryJobId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// La course d'une commande. UNE SEULE : c'est le garde-fou contre les
    /// republications de « commande prête », que deux vendeurs terminant à la même
    /// seconde peuvent déclencher deux fois.
    /// </summary>
    Task<DeliveryJob?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses qui attendent une (nouvelle) vague d'offres.
    ///
    /// DEUX CAS, ET LE SECOND MANQUAIT.
    ///
    ///  • OFFERED dont la vague en cours est arrivée à échéance : personne n'a répondu,
    ///    il faut élargir le rayon.
    ///  • CREATED : la course n'a JAMAIS été offerte, faute de coursier éligible au
    ///    moment de sa création. Elle ne figurait pas dans cette requête — donc rien ne
    ///    la reprenait, jamais. Une commande créée un dimanche matin, quand personne
    ///    n'est en ligne, restait sans livreur pour toujours, en silence.
    ///
    /// C'est exactement la panne qui ne se voit pas : aucune erreur, aucun journal, une
    /// ligne en base et un acheteur qui attend.
    /// </summary>
    Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les offres EN COURS pour ce coursier — ce que son application affiche à l'écran.
    ///
    /// Sans cette lecture, une offre poussée puis perdue (téléphone en veille, réseau
    /// coupé, application relancée) serait invisible jusqu'à son expiration. Le port
    /// temps réel est BEST-EFFORT par construction : il lui faut une lecture de rattrapage,
    /// sinon « best-effort » veut dire « perdue ».
    /// </summary>
    Task<IReadOnlyList<DeliveryJob>> ListOfferedToAsync(
        CourierId courierId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// La course que ce coursier est en train de faire, s'il y en a une.
    ///
    /// UNE SEULE PEUT L'ÊTRE : accepter passe le coursier en <c>OnDelivery</c>, statut
    /// qui l'exclut des offres suivantes. Si cette requête en rend deux, l'invariant est
    /// rompu quelque part en amont — pas ici.
    /// </summary>
    Task<DeliveryJob?> GetActiveForCourierAsync(
        CourierId courierId, CancellationToken cancellationToken = default);
}
