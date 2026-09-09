namespace HbaExpress.Dispatch.Application.Abstractions;

/// <summary>
/// Le canal SORTANT vers la marketplace.
///
/// Il prend le corps JSON DÉJÀ SÉRIALISÉ, tel qu'il a été mis en file, et non un objet à
/// sérialiser. C'est délibéré : le message a été figé au moment de la livraison, et c'est
/// exactement ces octets-là qu'il faut envoyer — et signer. Re-sérialiser à l'envoi
/// produirait une chaîne différente de celle qui a été décidée, et rendrait la file
/// dépendante de la version du code qui la draine.
/// </summary>
public interface IMarketplaceClient
{
    /// <summary>
    /// Annonce une course livrée : la marketplace crédite le coursier.
    /// LÈVE en cas d'échec — c'est le drain qui décide du réessai et de l'abandon.
    /// </summary>
    Task SendCourierDeliveryCompletedAsync(string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Annonce une course ÉCHOUÉE : la marketplace débloque l'expédition restée en
    /// préparation et décide de la suite (nouvelle tentative, remboursement, litige).
    ///
    /// Ce sens n'existait pas, et son absence figeait une commande pour toujours. Comme
    /// pour la livraison : LÈVE en cas d'échec — c'est le drain qui décide du réessai et
    /// de l'abandon.
    /// </summary>
    Task SendCourierDeliveryFailedAsync(string payloadJson, CancellationToken cancellationToken = default);
}
