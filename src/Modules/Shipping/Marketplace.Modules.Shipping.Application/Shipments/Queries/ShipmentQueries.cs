using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.Queries;

/// <summary>Récupère une expédition par son identifiant.</summary>
public sealed record GetShipmentQuery(Guid ShipmentId) : IQuery<ShipmentSummary>;

/// <summary>Liste les expéditions d'une commande.</summary>
public sealed record ListShipmentsByOrderQuery(Guid OrderId) : IQuery<IReadOnlyList<ShipmentSummary>>;

/// <summary>Liste les expéditions d'un vendeur.</summary>
public sealed record ListShipmentsBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<ShipmentSummary>>;

internal sealed class GetShipmentQueryHandler : IQueryHandler<GetShipmentQuery, ShipmentSummary>
{
    private readonly IShipmentRepository _repository;

    public GetShipmentQueryHandler(IShipmentRepository repository) => _repository = repository;

    public async Task<Result<ShipmentSummary>> Handle(GetShipmentQuery query, CancellationToken cancellationToken)
    {
        var shipment = await _repository.GetByIdAsync(new ShipmentId(query.ShipmentId), cancellationToken);
        return shipment is null
            ? Error.NotFound("shipping.not_found", "Expédition introuvable.")
            : ShipmentMapper.ToSummary(shipment);
    }
}

internal sealed class ListShipmentsByOrderQueryHandler : IQueryHandler<ListShipmentsByOrderQuery, IReadOnlyList<ShipmentSummary>>
{
    private readonly IShipmentRepository _repository;

    public ListShipmentsByOrderQueryHandler(IShipmentRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ShipmentSummary>>> Handle(ListShipmentsByOrderQuery query, CancellationToken cancellationToken)
    {
        var shipments = await _repository.ListByOrderAsync(query.OrderId, cancellationToken);
        IReadOnlyList<ShipmentSummary> summaries = shipments.Select(ShipmentMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListShipmentsBySellerQueryHandler : IQueryHandler<ListShipmentsBySellerQuery, IReadOnlyList<ShipmentSummary>>
{
    private readonly IShipmentRepository _repository;

    public ListShipmentsBySellerQueryHandler(IShipmentRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ShipmentSummary>>> Handle(ListShipmentsBySellerQuery query, CancellationToken cancellationToken)
    {
        var shipments = await _repository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<ShipmentSummary> summaries = shipments.Select(ShipmentMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
