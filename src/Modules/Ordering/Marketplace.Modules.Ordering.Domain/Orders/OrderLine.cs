using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Ordering.Domain.Orders;

/// <summary>
/// Données d'une ligne à figer au moment de la commande (snapshot prix issu de
/// Pricing). Sert d'entrée à la création de la commande.
/// </summary>
public sealed record OrderLineDraft(
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
    decimal CommissionRate,
    decimal ProviderFeeRate);

/// <summary>
/// Ligne de commande : un snapshot figé du prix au moment de l'achat (prix de
/// base, réductions par financeur, prix final). Les prix ne bougent plus après
/// la commande — base d'un payout vendeur auditable. Entité enfant de Order.
/// </summary>
public sealed class OrderLine : Entity<Guid>
{
    private OrderLine()
    {
    }

    internal OrderLine(Guid id, OrderLineDraft draft)
        : base(id)
    {
        OfferId = draft.OfferId;
        ProductId = draft.ProductId;
        SellerId = draft.SellerId;
        Sku = draft.Sku;
        ShipFromLocationId = draft.ShipFromLocationId;
        Quantity = draft.Quantity;
        UnitBasePrice = draft.UnitBasePrice;
        SellerDiscount = draft.SellerDiscount;
        PlatformDiscount = draft.PlatformDiscount;
        FinalUnitPrice = draft.FinalUnitPrice;
        CommissionRate = draft.CommissionRate;
        ProviderFeeRate = draft.ProviderFeeRate;
    }

    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitBasePrice { get; private set; }
    public decimal SellerDiscount { get; private set; }
    public decimal PlatformDiscount { get; private set; }
    public decimal FinalUnitPrice { get; private set; }

    // ---- Barème figé --------------------------------------------------------------
    //
    // LE PARTAGE DE L'ARGENT SE LIT ICI, PLUS JAMAIS DANS LA CONFIGURATION.
    //
    // Le prix acheteur a été construit par l'offre comme
    //     prix_vendeur × (1 + commission + provider).
    // Settlement refait le chemin à l'envers à la confirmation. Tant qu'il relisait les
    // taux courants, tout changement de barème réécrivait rétroactivement la
    // répartition des commandes tarifées à l'ancien barème : le vendeur touchait un net
    // différent de celui qu'il avait fixé, sans qu'aucune trace ne l'indique.
    //
    // Figés sur la ligne, les deux taux rendent la commande auto-suffisante — et un
    // changement de barème n'affecte que ce qui est vendu APRÈS lui.

    /// <summary>Taux de commission plateforme en vigueur au moment de la commande (0.10 = 10 %).</summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>Taux de frais provider en vigueur au moment de la commande (0.05 = 5 %).</summary>
    public decimal ProviderFeeRate { get; private set; }

    /// <summary>Total payé pour la ligne (prix final unitaire × quantité).</summary>
    public decimal LineTotal => FinalUnitPrice * Quantity;
}
