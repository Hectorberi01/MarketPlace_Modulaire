using MediatR;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Ordering. Délègue à la
/// requête GetOrder ; renvoie null si absent.
/// </summary>
internal sealed class OrderingModuleApi : IOrderingModuleApi
{
    private readonly ISender _sender;
    private readonly IOrderRepository _orders;

    public OrderingModuleApi(ISender sender, IOrderRepository orders)
    {
        _sender = sender;
        _orders = orders;
    }

    /// <summary>
    /// Lecture directe au repository, sans passer par MediatR : c'est un simple EXISTS
    /// sur index, appelé à CHAQUE valorisation de panier. Y interposer un pipeline de
    /// requête (validation, logging, transaction) coûterait plus que la requête elle-même.
    /// </summary>
    public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => _orders.HasPurchasedAsync(buyerId, cancellationToken);

    public async Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Somme des quantités vendues par ce vendeur sur les commandes encaissées
    /// (Confirmed / Delivered). Lecture directe au repository : c'est une agrégation
    /// simple, recalculée à chaque nouvelle vente — pas besoin du pipeline MediatR.
    /// </summary>
    public async Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListBySellerAsync(sellerId, cancellationToken);
        return orders
            .Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.Delivered)
            .SelectMany(o => o.Lines)
            .Where(l => l.SellerId == sellerId)
            .Sum(l => l.Quantity);
    }
}
