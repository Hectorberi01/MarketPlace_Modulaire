using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Carriers;

namespace Marketplace.Modules.Shipping.Application.Carriers;

internal static class CarrierMapper
{
    public static CarrierSummary ToSummary(Carrier c) => new(
        c.Id.Value,
        c.Name,
        c.Code,
        c.TrackingUrlTemplate,
        c.LogoUrl,
        c.IsActive,
        c.CreatedAtUtc);
}
