using MediatR;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Modules.Shipping.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Shipping.</summary>
internal sealed class ShippingModuleApi : IShippingModuleApi
{
    private readonly ISender _sender;

    public ShippingModuleApi(ISender sender) => _sender = sender;

    public async Task<IReadOnlyList<ShipmentSummary>> GetShipmentsByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new ListShipmentsByOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : Array.Empty<ShipmentSummary>();
    }
}
