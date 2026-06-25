namespace Marketplace.Modules.Shipping.Contracts;

/// <summary>
/// API in-process publique du module Shipping. Permet aux autres modules de
/// lire les expéditions d'une commande sans accéder à sa base.
/// </summary>
public interface IShippingModuleApi
{
    Task<IReadOnlyList<ShipmentSummary>> GetShipmentsByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
