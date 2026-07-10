using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Settlement.Domain.Earnings;

/// <summary>Identité forte d'un gain vendeur.</summary>
public readonly record struct SellerEarningId(Guid Value)
{
    public static SellerEarningId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statut d'un gain dans le cycle de reversement :
/// Accrued (comptabilisé à la confirmation, fonds en escrow) → Released
/// (livraison confirmée, escrow libéré, payable) → Settled (inclus dans un lot
/// de payout). Reversed couvre les retours/remboursements.
/// </summary>
public enum EarningStatus
{
    Accrued = 0,
    Released = 1,
    Settled = 2,
    Reversed = 3
}

/// <summary>
/// Gain vendeur accumulé pour une ligne de commande confirmée. Brut = prix
/// produit payé par l'acheteur pour la ligne ; net = brut − commission
/// plateforme − frais provider. Les réductions financées par la plateforme
/// n'entament pas le revenu vendeur. Sert de grand livre alimenté par les
/// events, batché ensuite en reversements.
/// </summary>
public sealed class SellerEarning : AggregateRoot<SellerEarningId>
{
    private SellerEarning()
    {
    }

    private SellerEarning(
        SellerEarningId id, Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
        : base(id)
    {
        OrderId = orderId;
        OfferId = offerId;
        SellerId = sellerId;
        ProductId = productId;
        GrossAmount = grossAmount;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        NetAmount = grossAmount - commissionAmount - providerFeeAmount;
        Currency = currency;
        Status = EarningStatus.Accrued;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal ProviderFeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public EarningStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public Guid? SettlementBatchId { get; private set; }

    public static Result<SellerEarning> Create(
        Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("settlement.seller_required", "Le vendeur est obligatoire.");
        }

        if (grossAmount < 0m || commissionAmount < 0m || providerFeeAmount < 0m)
        {
            return Error.Validation("settlement.amount_invalid", "Les montants ne peuvent pas être négatifs.");
        }

        return new SellerEarning(
            SellerEarningId.New(), orderId, offerId, sellerId, productId,
            grossAmount, commissionAmount, providerFeeAmount, currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Rend le gain payable à la livraison confirmée (escrow libéré). Idempotent ;
    /// ne s'applique qu'à un gain comptabilisé (Accrued).
    /// </summary>
    public void Release()
    {
        if (Status != EarningStatus.Accrued)
        {
            return;
        }

        Status = EarningStatus.Released;
        ReleasedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Rattache le gain à un lot de reversement (devient soldé).</summary>
    public void MarkSettled(Guid settlementBatchId)
    {
        Status = EarningStatus.Settled;
        SettlementBatchId = settlementBatchId;
    }
}
