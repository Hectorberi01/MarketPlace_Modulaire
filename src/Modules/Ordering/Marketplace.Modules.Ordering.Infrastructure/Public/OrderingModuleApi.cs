using MediatR;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Ordering.Contracts;

namespace Marketplace.Modules.Ordering.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Ordering. Délègue à la
/// requête GetOrder ; renvoie null si absent.
/// </summary>
internal sealed class OrderingModuleApi : IOrderingModuleApi
{
    private readonly ISender _sender;

    public OrderingModuleApi(ISender sender) => _sender = sender;

    public async Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
