namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Ce que la marketplace attend sur <c>POST /api/delivery/courses/completed</c>.
///
/// Type À NOUS, comme les charges entrantes : il a la forme de sa route, il vit ici, et
/// le jour où elle change, la correction tient dans ce fichier.
/// </summary>
/// <param name="DeliveryJobId">
/// LA CLÉ D'IDEMPOTENCE côté marketplace : deux annonces portant le même identifiant ne
/// paient qu'une fois. C'est elle qui rend le réessai du drain sans danger.
/// </param>
public sealed record CourierDeliveryCompletedPayload(
    Guid CourierId,
    Guid OrderId,
    Guid DeliveryJobId,
    decimal Amount,
    string Currency);

/// <summary>
/// Ce que la marketplace doit recevoir sur <c>POST /api/delivery/courses/failed</c> quand
/// une course n'a pas pu être livrée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE MESSAGE, UNE COURSE ÉCHOUÉE EST UN CUL-DE-SAC.
///
/// L'échec n'était écrit nulle part ailleurs qu'en base du service coursier : côté
/// marketplace l'expédition restait « en préparation » POUR TOUJOURS — séquestre non
/// tranchée, gains vendeur jamais versés, commande jamais close — et rien ne signalait
/// que quelque chose s'était arrêté.
///
/// AUCUN MONTANT ICI, CONTRAIREMENT À LA LIVRAISON. Ce message n'est pas un ordre de
/// paiement : il annonce un fait. Ce que la plateforme doit au coursier pour une course
/// tentée et non aboutie est une décision de dédommagement, pas une conséquence
/// mécanique — et elle ne se prend pas dans une charge utile.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="OrderId">
/// LA COMMANDE À DÉBLOQUER. C'est par elle que la marketplace retrouve l'expédition
/// figée ; sans elle, le message serait une notification qu'on ne peut rattacher à rien.
/// </param>
/// <param name="DeliveryJobId">
/// LA CLÉ D'IDEMPOTENCE, comme pour la livraison : le drain réessaie, et deux annonces
/// du même échec ne doivent produire qu'un seul traitement.
/// </param>
/// <param name="Reason">
/// La raison telle que la course la porte — normalisée par DeliveryJob.Fail, pas le texte
/// brut saisi par le coursier.
/// </param>
public sealed record CourierDeliveryFailedPayload(
    Guid CourierId,
    Guid OrderId,
    Guid DeliveryJobId,
    string Reason,
    DateTime FailedAtUtc);
