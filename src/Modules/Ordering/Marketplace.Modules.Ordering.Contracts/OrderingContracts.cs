namespace Marketplace.Modules.Ordering.Contracts;

/// <summary>Ligne de commande figée (snapshot prix par financeur).</summary>
public sealed record OrderLineSummary(
    Guid OfferId,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,
    // Barème figé au moment de la commande. C'est ce que Settlement lit pour répartir
    // le brut, au lieu de relire la configuration courante.
    decimal CommissionRate = 0m,
    decimal ProviderFeeRate = 0m);

/// <summary>
/// Adresse de livraison figée sur la commande (instantané).
///
/// <c>CommuneName</c> est résolu à la lecture depuis <c>CommuneCode</c> : seul le code est
/// stocké. <c>Landmark</c> est le point de repère — au Bénin, c'est l'information que le
/// livreur utilise réellement, bien avant la rue.
/// </summary>
public sealed record OrderShippingAddressSummary(
    string? Label,
    string? Recipient,
    string? CommuneCode,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    string? Phone);

/// <summary>Vue publique d'une commande.</summary>
public sealed record OrderSummary(
    Guid Id,
    Guid BuyerId,
    Guid CartId,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    IReadOnlyList<OrderLineSummary> Lines,
    OrderShippingAddressSummary? ShippingAddress = null,
    decimal ShippingFee = 0m,
    // Part réglée par la cagnotte de l'acheteur. GrandTotal reste la VALEUR de la
    // commande ; c'est AmountDue qui est encaissé auprès du prestataire.
    decimal WalletApplied = 0m,
    /// <summary>
    /// Date de livraison, ou nul. C'est ce dont le module Returns a besoin pour savoir
    /// si la fenêtre de retour est encore ouverte : sans elle, le délai devrait partir
    /// de la date de commande, donc pénaliser l'acheteur pour la lenteur d'une
    /// livraison qu'il ne contrôle pas.
    /// </summary>
    DateTime? DeliveredAtUtc = null,
    /// <summary>
    /// Taxe CONTENUE dans <paramref name="GrandTotal"/>, jamais ajoutée à lui : régime
    /// « TVA comprise ». Un consommateur qui additionnerait les deux facturerait la
    /// taxe une seconde fois. Sert la facture et la comptabilité, pas le calcul du
    /// montant dû.
    /// </summary>
    decimal TaxTotal = 0m,
    /// <summary>Taux retenu et figé au moment de la commande (0.18 = 18 %).</summary>
    decimal TaxRate = 0m)
{
    /// <summary>Part hors taxe du total. Un reste, comme dans l'agrégat.</summary>
    public decimal NetTotal => GrandTotal - TaxTotal;

    /// <summary>
    /// Montant restant à encaisser. Calculé plutôt que transporté : deux champs qui
    /// doivent s'accorder finissent toujours par diverger, et c'est le genre d'écart
    /// qui se solde par un client débité du mauvais montant.
    /// </summary>
    public decimal AmountDue => GrandTotal - WalletApplied;
}
