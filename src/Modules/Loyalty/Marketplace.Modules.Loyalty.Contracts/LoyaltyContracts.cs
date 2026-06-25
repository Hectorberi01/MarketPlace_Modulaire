namespace Marketplace.Modules.Loyalty.Contracts;

/// <summary>Mouvement de points.</summary>
public sealed record PointTransactionSummary(int Amount, string Reason, Guid? RelatedOrderId, DateTime CreatedAtUtc);

/// <summary>Vue publique d'un compte de fidélité.</summary>
public sealed record LoyaltyAccountSummary(
    Guid Id, Guid UserId, int PointsBalance, int LifetimePoints, string Tier, IReadOnlyList<PointTransactionSummary> Transactions);
