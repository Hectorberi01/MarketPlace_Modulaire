namespace Marketplace.Modules.Reviews.Contracts;

/// <summary>Vue publique d'un avis.</summary>
public sealed record ReviewSummary(
    Guid Id,
    Guid ProductId,
    Guid SellerId,
    Guid BuyerId,
    int Rating,
    string Title,
    string Body,
    bool IsVerifiedPurchase,
    string Status,
    DateTime CreatedAtUtc,
    string? SellerReply,
    DateTime? SellerRepliedAtUtc);

/// <summary>Note agrégée d'un produit.</summary>
public sealed record ProductRatingSummary(Guid ProductId, double Average, int Count);
