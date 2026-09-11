namespace Marketplace.Modules.Inventory.Contracts;

/// <summary>Vue publique d'un article de stock.</summary>
public sealed record InventoryItemSummary(
    Guid Id,
    string Sku,
    Guid LocationId,
    int OnHand,
    int Reserved,
    int Available,
    int ReorderThreshold,
    bool IsLowStock);

/// <summary>Disponibilité agrégée d'un SKU (toutes localisations).</summary>
public sealed record AvailabilitySummary(string Sku, int TotalAvailable);

/// <summary>Vue publique d'un lieu d'expédition.</summary>
public sealed record FulfillmentLocationSummary(
    Guid Id,
    string Type,
    Guid? OwnerId,
    string CommuneCode,
    string CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line,
    string CountryCode,
    double? Latitude,
    double? Longitude);

/// <summary>
/// Les lieux d'expédition rendus, ET COMBIEN IL Y EN A EN TOUT.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// SANS LE TOTAL, UNE LISTE COUPÉE SE LIT COMME UNE DONNÉE INCOHÉRENTE.
///
/// La console de supervision du stock rattache un article en rupture à sa boutique par
/// la chaîne article → emplacement → vendeur. Elle ne peut le faire que pour les
/// emplacements qu'elle a reçus. Au-delà de la borne, la chaîne casse et la colonne
/// « Boutique » affiche « emplacement introuvable » — ce qui se lit « la base est
/// abîmée », alors que la cause est « la liste a été tronquée ». L'administrateur
/// cherche un problème de données qui n'existe pas, et la rupture de stock, elle, reste
/// là.
///
/// Avec ce total, l'écran dit laquelle des deux situations il a sous les yeux.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Items">Les emplacements rendus, dans la limite de la borne.</param>
/// <param name="Total">Le nombre d'emplacements existants.</param>
public sealed record FulfillmentLocationList(IReadOnlyList<FulfillmentLocationSummary> Items, int Total);
