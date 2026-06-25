using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Loyalty.Domain.Accounts;

/// <summary>Identité forte d'un compte de fidélité.</summary>
public readonly record struct LoyaltyAccountId(Guid Value)
{
    public static LoyaltyAccountId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Palier de fidélité.</summary>
public enum LoyaltyTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2
}

/// <summary>Motif d'un mouvement de points.</summary>
public enum PointReason
{
    Purchase = 0,
    Cashback = 1,
    Referral = 2,
    Expiry = 3,
    Redemption = 4
}

/// <summary>Mouvement de points (gain à l'achat, dépense en cashback, parrainage…). Entité enfant.</summary>
public sealed class PointTransaction : Entity<Guid>
{
    private PointTransaction()
    {
    }

    internal PointTransaction(Guid id, int amount, PointReason reason, Guid? relatedOrderId)
        : base(id)
    {
        Amount = amount;
        Reason = reason;
        RelatedOrderId = relatedOrderId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public int Amount { get; private set; }
    public PointReason Reason { get; private set; }
    public Guid? RelatedOrderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>
/// Compte de fidélité d'un acheteur : solde de points, palier et historique. Le
/// palier dépend des points cumulés gagnés (stable, ne baisse pas à la dépense).
/// Agrégat racine : possède ses mouvements.
/// </summary>
public sealed class LoyaltyAccount : AggregateRoot<LoyaltyAccountId>
{
    private const int SilverThreshold = 1000;
    private const int GoldThreshold = 5000;

    private readonly List<PointTransaction> _transactions = new();

    private LoyaltyAccount()
    {
    }

    private LoyaltyAccount(LoyaltyAccountId id, Guid userId)
        : base(id)
    {
        UserId = userId;
        PointsBalance = 0;
        LifetimePoints = 0;
        Tier = LoyaltyTier.Bronze;
    }

    public Guid UserId { get; private set; }
    public int PointsBalance { get; private set; }
    public int LifetimePoints { get; private set; }
    public LoyaltyTier Tier { get; private set; }
    public IReadOnlyCollection<PointTransaction> Transactions => _transactions.AsReadOnly();

    public static Result<LoyaltyAccount> Create(Guid userId)
        => userId == Guid.Empty
            ? Error.Validation("loyalty.user_required", "L'utilisateur est obligatoire.")
            : new LoyaltyAccount(LoyaltyAccountId.New(), userId);

    /// <summary>Vrai si des points ont déjà été crédités pour cette commande (idempotence).</summary>
    public bool HasEarnedForOrder(Guid orderId)
        => _transactions.Any(t => t.RelatedOrderId == orderId && t.Reason == PointReason.Purchase);

    public Result EarnPoints(int amount, PointReason reason, Guid? relatedOrderId)
    {
        if (amount <= 0)
        {
            return Result.Failure(Error.Validation("loyalty.amount_invalid", "Le gain de points doit être positif."));
        }

        _transactions.Add(new PointTransaction(Guid.NewGuid(), amount, reason, relatedOrderId));
        PointsBalance += amount;
        LifetimePoints += amount;
        RecomputeTier();
        return Result.Success();
    }

    public Result RedeemPoints(int amount)
    {
        if (amount <= 0)
        {
            return Result.Failure(Error.Validation("loyalty.amount_invalid", "Le montant à dépenser doit être positif."));
        }

        if (PointsBalance < amount)
        {
            return Result.Failure(Error.Conflict("loyalty.insufficient", "Solde de points insuffisant."));
        }

        _transactions.Add(new PointTransaction(Guid.NewGuid(), -amount, PointReason.Redemption, null));
        PointsBalance -= amount;
        return Result.Success();
    }

    private void RecomputeTier()
        => Tier = LifetimePoints >= GoldThreshold ? LoyaltyTier.Gold
            : LifetimePoints >= SilverThreshold ? LoyaltyTier.Silver
            : LoyaltyTier.Bronze;
}
