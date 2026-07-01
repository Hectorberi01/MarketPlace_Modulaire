using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Application.Orders.Queries;

/// <summary>Récupère une commande par son identifiant.</summary>
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderSummary>;

/// <summary>Liste les commandes d'un acheteur.</summary>
public sealed record ListMyOrdersQuery(Guid BuyerId) : IQuery<IReadOnlyList<OrderSummary>>;

/// <summary>Liste les commandes comportant au moins une ligne d'un vendeur (back-office vendeur).</summary>
public sealed record ListOrdersBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<OrderSummary>>;

/// <summary>Liste toutes les commandes de la plateforme (back-office admin).</summary>
public sealed record ListAllOrdersQuery : IQuery<IReadOnlyList<OrderSummary>>;

internal sealed class ListAllOrdersQueryHandler : IQueryHandler<ListAllOrdersQuery, IReadOnlyList<OrderSummary>>
{
    private readonly IOrderRepository _repository;

    public ListAllOrdersQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<OrderSummary>>> Handle(ListAllOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = await _repository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<OrderSummary> summaries = orders.Select(OrderMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListOrdersBySellerQueryHandler : IQueryHandler<ListOrdersBySellerQuery, IReadOnlyList<OrderSummary>>
{
    private readonly IOrderRepository _repository;

    public ListOrdersBySellerQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<OrderSummary>>> Handle(ListOrdersBySellerQuery query, CancellationToken cancellationToken)
    {
        var orders = await _repository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<OrderSummary> summaries = orders.Select(OrderMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderSummary>
{
    private readonly IOrderRepository _repository;

    public GetOrderQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<OrderSummary>> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(new OrderId(query.OrderId), cancellationToken);
        return order is null
            ? Error.NotFound("ordering.not_found", "Commande introuvable.")
            : OrderMapper.ToSummary(order);
    }
}

internal sealed class ListMyOrdersQueryHandler : IQueryHandler<ListMyOrdersQuery, IReadOnlyList<OrderSummary>>
{
    private readonly IOrderRepository _repository;

    public ListMyOrdersQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<OrderSummary>>> Handle(ListMyOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = await _repository.ListByBuyerAsync(query.BuyerId, cancellationToken);
        IReadOnlyList<OrderSummary> summaries = orders.Select(OrderMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
