using MediatR;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Shipping.Application.Zones;
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

    public async Task<IReadOnlyList<ShipmentDeliveryCode>> GetDeliveryCodesByOrderAsync(
        Guid orderId, Guid buyerId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ListDeliveryCodesByOrderQuery(orderId, buyerId), cancellationToken);
        return result.IsSuccess ? result.Value : Array.Empty<ShipmentDeliveryCode>();
    }

    public async Task<IReadOnlyList<ShippingRateOption>> GetRatesForCommuneAsync(
        string? communeCode, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetShippingRatesQuery(communeCode), cancellationToken);

        // Un échec de lecture ne doit pas se traduire par « aucune livraison
        // possible » à l'écran : la base est indisponible, pas la livraison. Le
        // secours prend le relais, exactement comme pour une grille vide.
        return result.IsSuccess ? result.Value : FallbackRates.Options;
    }
}
