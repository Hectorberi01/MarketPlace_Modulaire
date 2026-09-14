namespace Marketplace.Modules.Cart.Contracts;

/// <summary>Ligne de panier valorisée : prix de base + réductions tracées par financeur.</summary>
public sealed record CartLineSummary(
    Guid OfferId,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,
    string Currency,
    // Barème figé à l'ajout au panier, transmis tel quel à la commande.
    decimal CommissionRate = 0m,
    decimal ProviderFeeRate = 0m);

/// <summary>Panier valorisé (snapshot lignes + totaux calculés via Pricing).</summary>
public sealed record CartSummary(
    Guid CartId,
    Guid BuyerId,
    string Currency,
    string Status,
    IReadOnlyList<CartLineSummary> Lines,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    /// <summary>
    /// TOTAL À PAYER, frais de service COMPRIS.
    ///
    /// Le frais y est inclus délibérément : tout ce qui lit ce champ — couverture par
    /// la cagnotte, montant annoncé au checkout, contrôle « le montant a-t-il changé »
    /// — doit voir ce que l'acheteur paiera réellement. L'exclure aurait obligé chaque
    /// appelant à penser à le rajouter, et le premier qui l'oublie encaisse 2 % de
    /// moins sans que rien ne le signale.
    /// </summary>
    decimal GrandTotal,
    /// <summary>Code promo appliqué au panier, ou null. Ordering le fige dans la commande au checkout.</summary>
    string? PromotionCode = null,
    /// <summary>
    /// Frais de service de la plateforme, calculé sur le total des articles remises
    /// déduites. Montant ANNONCÉ : celui qui fait foi est figé sur la commande au
    /// checkout, recalculé par l'agrégat à partir de ses propres lignes.
    /// </summary>
    decimal PlatformFee = 0m,
    /// <summary>Taux appliqué (0.02 = 2 %), pour l'afficher à côté du montant.</summary>
    decimal PlatformFeeRate = 0m)
{
    /// <summary>
    /// Total des ARTICLES seuls, remises déduites — le sous-total que la vitrine
    /// affiche au-dessus de la ligne de frais.
    /// </summary>
    public decimal ItemsTotal => GrandTotal - PlatformFee;
}
