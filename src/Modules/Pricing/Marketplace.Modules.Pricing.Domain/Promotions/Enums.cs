namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>Qui crée la promotion.</summary>
public enum PromotionOwnerType
{
    Seller = 0,
    Platform = 1
}

/// <summary>Qui finance (paie) la réduction — détermine la répartition au payout.</summary>
public enum FundedBy
{
    Seller = 0,
    Platform = 1,
    Shared = 2
}

/// <summary>Type de réduction.</summary>
public enum PromotionType
{
    Percentage = 0,
    FixedAmount = 1,
    FreeShipping = 2,
    BuyXGetY = 3
}

/// <summary>Périmètre d'application.</summary>
public enum PromotionScopeType
{
    Product = 0,
    Category = 1,
    Seller = 2,
    Cart = 3,
    Global = 4
}

/// <summary>Statut d'une promotion.</summary>
public enum PromotionStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Expired = 3
}
