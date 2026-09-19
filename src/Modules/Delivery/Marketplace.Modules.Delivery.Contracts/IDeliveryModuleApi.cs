namespace Marketplace.Modules.Delivery.Contracts;

/// <summary>
/// API in-process publique du module Delivery.
///
/// Sa première raison d'être : permettre à Settlement de savoir OÙ verser l'argent d'un
/// coursier. Le résolveur de versement portait jusqu'ici un commentaire disant « le
/// livreur n'a pas encore de module qui porte ses coordonnées » et renvoyait null ;
/// c'est ce trou que cette interface a refermé.
///
/// Elle porte depuis une seconde chose, de nature différente : la POSITION des coursiers,
/// qui n'appartient pas à ce module et n'y sera jamais rangée. Voir GetFleetAsync.
/// </summary>
public interface IDeliveryModuleApi
{
    Task<CourierSummary?> GetCourierAsync(Guid courierId, CancellationToken cancellationToken = default);

    /// <summary>Le coursier rattaché à un compte utilisateur (écrans « mon compte coursier »).</summary>
    Task<CourierSummary?> GetCourierByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les coursiers présents sur le terrain, avec leur dernière position connue.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// RIEN N'EST STOCKÉ ICI, ET C'EST LA CONDITION POUR QUE CETTE MÉTHODE EXISTE.
    ///
    /// La console d'administration annonçait jusqu'ici qu'elle ne montrerait jamais les
    /// positions, au motif que « les afficher supposerait de les recopier, et il y
    /// aurait alors deux vérités sur la même personne ». L'argument tient contre une
    /// COPIE : une table de positions dans la marketplace divergerait de celle du
    /// dispatch dans la minute, et personne ne saurait laquelle croire.
    ///
    /// Il ne tient pas contre un RELAIS. Cet appel interroge le service de dispatch à
    /// l'instant de la demande, transmet ce qu'il répond, et n'en garde rien : ni ligne
    /// en base, ni cache. Il n'y a toujours qu'une vérité, et elle est là-bas.
    ///
    /// LE RELAIS NE LÈVE PAS QUAND LE DISPATCH EST INJOIGNABLE. C'est l'exception au
    /// contrat de ce canal, où tout échec lève pour que l'outbox réessaie. Ici,
    /// l'appelant est un écran : une exception lui rendrait une page en erreur, alors
    /// que le reste de ce qu'elle affiche — les emplacements vendeurs, les zones — est
    /// parfaitement disponible. L'instantané dit donc qu'il n'a PAS pu être pris, et
    /// l'écran a de quoi le montrer.
    ///
    /// UNE FLOTTE VIDE ET UNE FLOTTE INCONNUE NE SE RESSEMBLENT PAS, elles s'opposent :
    /// l'une dit « personne ne roule », l'autre « je ne sais pas qui roule ». Sans le
    /// drapeau, les deux donnent la même carte vide, et l'administrateur lit la plus
    /// rassurante des deux.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<CourierFleetSnapshot> GetFleetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Où en est la livraison d'une commande, et où est le coursier qui la porte.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// DESTINÉE À L'ACHETEUR, ET NON À LA SUPERVISION. LA DIFFÉRENCE EST DANS CE QUI
    /// N'EST PAS RENDU.
    ///
    /// <see cref="GetFleetAsync"/> rend TOUS les coursiers présents, avec leur
    /// identifiant et leur disponibilité. Celle-ci rend UN coursier, sans identifiant —
    /// qui permettrait de le reconnaître d'une commande à l'autre — et sans
    /// disponibilité, qui dirait à l'acheteur combien d'autres courses attendent l'homme
    /// qui lui apporte son colis.
    ///
    /// Et elle s'arrête AVEC la course : plus de position une fois livrée. Suivre un
    /// colis est une raison de savoir où est un coursier ; l'avoir eue une fois n'en est
    /// pas une de le garder.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// L'APPELANT DOIT AVOIR VÉRIFIÉ LA POSSESSION DE LA COMMANDE. <paramref
    /// name="buyerId"/> n'est pas décoratif : il part dans le corps signé et le service
    /// de dispatch le recompare à l'acheteur de la course. Mais c'est une SECONDE
    /// serrure, pas la première.
    /// </summary>
    Task<OrderDeliveryTracking> GetOrderTrackingAsync(
        Guid orderId, Guid buyerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le suivi d'une commande, ET s'il a pu être demandé.
/// </summary>
/// <param name="Reachable">
/// Faux quand le service de dispatch n'a pas pu être interrogé. Tout le reste est alors
/// vide, et ce vide ne signifie RIEN sur le terrain.
/// </param>
/// <param name="Failure">
/// Phrase destinée à l'écran, non nulle uniquement quand <c>Reachable</c> est faux. Elle
/// nomme la panne sans exposer d'adresse interne ni de trace.
/// </param>
/// <param name="ByCourier">
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHAMP QUI SÉPARE « RIEN À MONTRER » DE « ÇA NE MARCHE PAS ».
///
/// Faux veut dire : cette commande n'a pas de course de coursier. C'est le cas d'une
/// commande partie par transporteur, et celui d'une commande pas encore prête — deux
/// situations parfaitement normales, et majoritaires. L'écran n'affiche alors simplement
/// pas de carte.
///
/// Sans ce champ, l'absence de coursier serait indiscernable d'un service en panne, et
/// l'acheteur d'un colis qui voyage très bien lirait un message d'incident.
/// ═════════════════════════════════════════════════════════════════════════════
/// </param>
/// <param name="Status">Statut de la course : « Offered », « Accepted », « InTransit »…</param>
/// <param name="Leg">
/// « ToPickup » — le coursier va chercher le colis ; « ToBuyer » — il l'apporte ;
/// « None » — rien en cours. CALCULÉ PAR LE DISPATCH : le déduire du statut dans chaque
/// application garantirait que le web et le mobile divergent au premier statut ajouté.
/// </param>
/// <param name="PickupsDone">Boutiques déjà traitées — colis pris, ou incident constaté.</param>
/// <param name="PositionMaxAgeSeconds">
/// Âge au-delà duquel le dispatch lui-même ne croit plus le coursier là où il dit être.
/// L'écran DOIT s'en servir pour cesser d'affirmer une présence, plutôt que de recopier
/// une valeur qui divergerait au premier réglage.
/// </param>
/// <param name="Courier">Nul tant que personne n'a pris la course, et dès qu'elle est finie.</param>
public sealed record OrderDeliveryTracking(
    bool Reachable,
    string? Failure,
    DateTime ObservedAtUtc,
    bool ByCourier,
    string? Status,
    string? Leg,
    int PickupsDone,
    int PickupsTotal,
    double? DropoffLatitude,
    double? DropoffLongitude,
    DateTime? AcceptedAtUtc,
    DateTime? CompletedAtUtc,
    int PositionMaxAgeSeconds,
    TrackedCourierPosition? Courier);

/// <summary>
/// Le coursier tel que l'acheteur le voit : un nom, un véhicule, un point, un instant.
///
/// PAS D'IDENTIFIANT, DÉLIBÉRÉMENT — voir <see cref="IDeliveryModuleApi.GetOrderTrackingAsync"/>.
/// </summary>
/// <param name="Vehicle">« Motorcycle », « Bicycle », « Car »… : l'acheteur guette une moto.</param>
/// <param name="PositionAtUtc">
/// QUAND la position a été relevée. À AFFICHER : un repère sur une carte a la même allure
/// qu'il date de dix secondes ou d'une heure, et un coursier entré dans une zone sans
/// réseau continuerait sinon d'« avancer » là où il a été vu pour la dernière fois.
/// </param>
public sealed record TrackedCourierPosition(
    string DisplayName,
    string Vehicle,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);

/// <summary>
/// Ce que le service de dispatch a répondu, ET s'il a répondu.
/// </summary>
/// <param name="Reachable">
/// Faux quand le service n'a pas pu être interrogé. <c>Couriers</c> est alors vide, et
/// cette liste vide ne signifie RIEN sur le terrain.
/// </param>
/// <param name="Failure">
/// Phrase destinée à l'administrateur, non nulle uniquement quand <c>Reachable</c> est
/// faux. Elle nomme la panne sans exposer d'adresse interne ni de trace.
/// </param>
/// <param name="ObservedAtUtc">
/// Instant du relevé, posé ICI et non par le dispatch : c'est la fraîcheur de l'APPEL
/// qu'on atteste, et c'est elle qui permet à l'écran de dire « il y a 12 secondes ».
/// </param>
/// <param name="OfferMaxAgeSeconds">
/// Âge de position au-delà duquel le service de dispatch cesse d'OFFRIR des courses au
/// coursier. Relayé depuis le service, jamais recopié : les deux seuils sont différents
/// à dessein, et une console qui les redéfinirait de son côté finirait par afficher
/// comme disponibles des coursiers auxquels le dispatch ne propose déjà plus rien.
/// Vaut zéro quand <c>Reachable</c> est faux — on ne les connaît alors pas.
/// </param>
/// <param name="ActionMaxAgeSeconds">
/// Âge au-delà duquel le coursier ne peut plus AGIR sur la course qu'il porte déjà : ni
/// ramasser, ni partir, ni livrer. Plus permissif que le précédent, à dessein.
/// </param>
/// <param name="Couriers">
/// Les coursiers sur le terrain. VIDE quand <c>Reachable</c> est faux, et cette liste
/// vide ne dit alors rien du terrain.
/// </param>
public sealed record CourierFleetSnapshot(
    bool Reachable,
    string? Failure,
    DateTime ObservedAtUtc,
    int OfferMaxAgeSeconds,
    int ActionMaxAgeSeconds,
    IReadOnlyList<CourierPosition> Couriers);

/// <param name="CourierId">
/// Identifiant du coursier DANS LE SERVICE DE DISPATCH. Ce n'est pas le
/// <c>CourierSummary.Id</c> de ce module : le rapprochement passe par
/// <c>CourierSummary.ExternalRef</c>, et il n'est pas fait ici.
/// </param>
/// <param name="DisplayName">Nom affiché du coursier, tel que la marketplace l'a annoncé au dispatch.</param>
/// <param name="Vehicle">Type de véhicule déclaré (« Moto », « Velo », « Voiture »…).</param>
/// <param name="Availability">« Online » ou « OnDelivery ». Les hors ligne ne sont pas renvoyés.</param>
/// <param name="Latitude">Degrés décimaux, WGS 84.</param>
/// <param name="Longitude">Degrés décimaux, WGS 84.</param>
/// <param name="PositionAtUtc">
/// Âge de la position, indispensable à l'affichage : au-delà de cinq minutes le service
/// de dispatch lui-même cesse de considérer le coursier joignable. Un repère sur une
/// carte a la même allure qu'il date de dix secondes ou de deux heures.
/// </param>
public sealed record CourierPosition(
    Guid CourierId,
    string DisplayName,
    string Vehicle,
    string Availability,
    double Latitude,
    double Longitude,
    DateTime PositionAtUtc);
