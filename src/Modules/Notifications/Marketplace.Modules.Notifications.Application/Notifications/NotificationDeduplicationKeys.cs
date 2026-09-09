namespace Marketplace.Modules.Notifications.Application.Notifications;

/// <summary>
/// Clés d'unicité des notifications « une seule fois »
/// (<see cref="NotificationDispatcher.NotifyOnceAsync"/>).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE CLÉ DÉCRIT UN FAIT ET SON DESTINATAIRE, JAMAIS UNE TENTATIVE.
///
/// C'est toute la condition pour que l'idempotence tienne : deux tentatives du MÊME
/// message doivent produire la MÊME chaîne. Y glisser quoi que ce soit qui varie d'une
/// tentative à l'autre — un horodatage, un identifiant de message d'outbox, un GUID —
/// rendrait la contrainte d'unicité silencieusement inopérante, et le défaut
/// reviendrait sans que rien ne le signale.
///
/// Le destinataire fait partie de la clé : une commande multi-vendeurs produit une
/// notification par vendeur, toutes légitimes. Sans lui, le premier vendeur prévenu
/// bloquerait tous les autres — un bug pire que celui qu'on corrige.
///
/// Les clés sont centralisées ici plutôt qu'écrites dans chaque handler : deux
/// formulations divergentes du même fait (« order-confirmed » ici, « orderconfirmed »
/// là) ne se collisionneraient pas, et le doublon repasserait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class NotificationDeduplicationKeys
{
    /// <summary>« Commande confirmée » adressée à l'acheteur — une par commande.</summary>
    public static string OrderConfirmedBuyer(Guid orderId)
        => $"order-confirmed:buyer:{orderId}";

    /// <summary>
    /// « Nouvelle commande à préparer » adressée à un vendeur — une par commande ET par
    /// vendeur, puisqu'une commande peut en concerner plusieurs.
    /// </summary>
    public static string OrderConfirmedSeller(Guid orderId, Guid sellerUserId)
        => $"order-confirmed:seller:{orderId}:{sellerUserId}";

    /// <summary>
    /// « Votre colis n'a pas pu être enlevé » adressée à un vendeur — une par COURSE et
    /// par vendeur.
    ///
    /// LA CLÉ PORTE LA COURSE, PAS LA COMMANDE. Une commande peut voir plusieurs courses
    /// échouer l'une après l'autre : première tentative infructueuse, repli reproposé,
    /// second abandon. Chacune est un fait NOUVEAU que le vendeur doit connaître.
    /// Dédupliquer sur la commande ferait taire tous les échecs après le premier — et le
    /// vendeur croirait sa réexpédition partie.
    /// </summary>
    public static string CourierDeliveryFailedSeller(Guid deliveryJobId, Guid sellerUserId)
        => $"courier-delivery-failed:seller:{deliveryJobId}:{sellerUserId}";
}
