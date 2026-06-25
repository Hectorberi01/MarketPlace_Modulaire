namespace Marketplace.Modules.Ordering.Contracts;

/// <summary>
/// API in-process publique du module Ordering. Permet aux autres modules
/// (Payments, Shipping…) de lire une commande sans accéder à sa base.
/// </summary>
public interface IOrderingModuleApi
{
    Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
