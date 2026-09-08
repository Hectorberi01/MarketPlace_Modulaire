using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Application.Couriers;

internal static class CourierMapper
{
    public static CourierSummary ToSummary(Courier courier) => new(
        courier.Id.Value,
        courier.UserId,
        courier.DisplayName,
        courier.Phone,
        courier.Status.ToString(),
        courier.CanBePaid,
        courier.ExternalRef,
        new CourierPayoutSummary(
            courier.Payout.Provider, courier.Payout.AccountNumber, courier.Payout.AccountName),
        courier.CreatedAtUtc);
}
