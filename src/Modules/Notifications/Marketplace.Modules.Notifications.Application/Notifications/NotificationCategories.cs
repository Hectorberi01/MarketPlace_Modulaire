namespace Marketplace.Modules.Notifications.Application.Notifications;

/// <summary>
/// Catégories de notification proposées au réglage vendeur, et correspondance
/// avec le « relatedType » porté par chaque notification. Une catégorie inconnue
/// (relatedType non mappé) n'est jamais coupable : le push part toujours.
/// </summary>
public static class NotificationCategories
{
    public const string Orders = "orders";     // commandes & expéditions
    public const string Returns = "returns";   // retours & litiges
    public const string Reviews = "reviews";   // avis
    public const string Messages = "messages"; // messagerie
    public const string Account = "account";   // compte & paiements

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// ANNONCES ET OPÉRATIONS COMMERCIALES — LA SEULE CATÉGORIE NON TRANSACTIONNELLE.
    ///
    /// Les cinq autres décrivent des faits qui concernent l'utilisateur : sa commande,
    /// son litige, son avis, son message, son compte. Celle-ci décrit ce que la
    /// plateforme a décidé de lui dire.
    ///
    /// ELLE DOIT EXISTER AVANT LA DIFFUSION, et c'est tout l'enjeu. `FromRelatedType`
    /// rend `null` pour un type inconnu, et l'appelant lit ce `null` comme « aucune
    /// préférence ne s'applique, on envoie » : une diffusion commerciale portant un type
    /// non mappé aurait donc traversé TOUTES les coupures de tous les utilisateurs. Le
    /// défaut le plus permissif du système se serait appliqué au message le moins
    /// souhaité.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public const string Announcements = "announcements";

    public static readonly IReadOnlyList<string> All = new[] { Orders, Returns, Reviews, Messages, Account, Announcements };

    public static bool IsKnown(string category) => All.Contains(category);

    /// <summary>Catégorie d'un relatedType (null si non mappé → toujours envoyé).</summary>
    public static string? FromRelatedType(string? relatedType) => relatedType?.Trim().ToLowerInvariant() switch
    {
        // « inventory » : rupture de stock. Rangé avec les commandes — c'est la file
        // de travail quotidienne du vendeur — mais il porte un identifiant d'ARTICLE
        // DE STOCK, pas de commande. Le type était « order », ce qui envoyait les
        // clients ouvrir une fiche commande introuvable.
        "order" or "shipment" or "inventory" => Orders,
        "return" or "dispute" => Returns,
        "review" => Reviews,
        "message" or "conversation" => Messages,
        "seller" or "payout" or "wallet" or "payment" => Account,
        // « campaign » : l'annonce d'un événement commercial. « announcement » : une
        // annonce d'exploitation diffusée depuis la console.
        "announcement" or "campaign" => Announcements,
        _ => null,
    };
}
