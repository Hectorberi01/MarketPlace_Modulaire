namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>Identité forte d'une promotion.</summary>
public readonly record struct PromotionId(Guid Value)
{
    public static PromotionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
