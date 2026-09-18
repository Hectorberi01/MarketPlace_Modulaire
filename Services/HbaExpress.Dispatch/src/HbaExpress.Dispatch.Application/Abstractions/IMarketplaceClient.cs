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

    /// <summary>
    /// Annonce une offre proposée : la marketplace envoie la notification push.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA SEULE MÉTHODE DE CE CANAL QUI NE PASSE PAS PAR LA FILE.
    ///
    /// Les deux autres portent un fait acquis et doivent finir par arriver ; le drain
    /// les réessaie. Celle-ci porte une proposition qui vit vingt-cinq secondes :
    /// rejouée plus tard, elle sonnerait pour une course déjà prise. Elle part donc en
    /// direct, une fois.
    ///
    /// LÈVE comme les autres — mais ici personne ne réessaie : l'appelant journalise et
    /// passe. C'est du BEST-EFFORT au sens strict, et l'offre reste valable en base.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task SendCourierOfferAsync(string payloadJson, CancellationToken cancellationToken = default);
}
