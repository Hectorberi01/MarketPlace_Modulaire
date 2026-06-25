using Marketplace.Modules.Pricing.Contracts;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Promotions;

internal static class PromotionMapper
{
    public static PromotionSummary ToSummary(Promotion p) => new(
        p.Id.Value,
        p.OwnerType.ToString(),
        p.OwnerId,
        p.FundedBy.ToString(),
        p.Type.ToString(),
        p.Value,
        p.ScopeType.ToString(),
        p.Targets.ToList(),
        p.Code,
        p.Period.StartUtc,
        p.Period.EndUtc,
        p.UsageLimit,
        p.UsedCount,
        p.Status.ToString());
}
