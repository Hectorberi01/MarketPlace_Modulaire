namespace Marketplace.Modules.Ordering.Contracts;

/// <summary>
/// API in-process publique du module Ordering. Permet aux autres modules
/// (Payments, Shipping…) de lire une commande sans accéder à sa base.
/// </summary>
public interface IOrderingModuleApi
{
    Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ passé une commande (hors commandes échouées) ?
    ///
    /// Sert exclusivement aux promotions « première commande ». Sans cette lecture,
    /// `CartPricer` passait `IsFirstOrder: false` en dur, et TOUTE promo de bienvenue
    /// était silencieusement inapplicable.
    /// </summary>
    Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
