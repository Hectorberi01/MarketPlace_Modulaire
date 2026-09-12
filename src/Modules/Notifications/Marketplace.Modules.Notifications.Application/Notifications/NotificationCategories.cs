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

    public static readonly IReadOnlyList<string> All = new[] { Orders, Returns, Reviews, Messages, Account };

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
        _ => null,
    };
}
