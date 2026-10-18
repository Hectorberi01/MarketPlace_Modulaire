using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Application.Shipments.Queries;

/// <summary>Récupère une expédition par son identifiant.</summary>
public sealed record GetShipmentQuery(Guid ShipmentId) : IQuery<ShipmentSummary>;

/// <summary>Liste les expéditions d'une commande.</summary>
public sealed record ListShipmentsByOrderQuery(Guid OrderId) : IQuery<IReadOnlyList<ShipmentSummary>>;

/// <summary>
/// Les codes de remise d'une commande, POUR SON ACHETEUR.
///
/// [BuyerId] est contrôlé ici en plus du garde du BFF : c'est un secret, et il ne
/// dépend pas d'un seul verrou. Même précaution que `GetOrderTrackingAsync`, qui prend
/// l'acheteur pour la même raison.
///
/// Une commande à plusieurs vendeurs a PLUSIEURS expéditions, donc plusieurs codes :
/// l'acheteur donne à chacun le sien.
/// </summary>
public sealed record ListDeliveryCodesByOrderQuery(Guid OrderId, Guid BuyerId)
    : IQuery<IReadOnlyList<ShipmentDeliveryCode>>;

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

internal sealed class ListDeliveryCodesByOrderQueryHandler
    : IQueryHandler<ListDeliveryCodesByOrderQuery, IReadOnlyList<ShipmentDeliveryCode>>
{
    private readonly IShipmentRepository _repository;

    public ListDeliveryCodesByOrderQueryHandler(IShipmentRepository repository)
        => _repository = repository;

    public async Task<Result<IReadOnlyList<ShipmentDeliveryCode>>> Handle(
        ListDeliveryCodesByOrderQuery query, CancellationToken cancellationToken)
    {
        var shipments = await _repository.ListByOrderAsync(query.OrderId, cancellationToken);

        IReadOnlyList<ShipmentDeliveryCode> codes = shipments
            // L'ACHETEUR DE L'EXPÉDITION, pas seulement celui de la commande. La
            // vérification est redondante avec le garde du BFF, et c'est voulu.
            .Where(s => s.BuyerId == query.BuyerId)
            // RIEN APRÈS LA REMISE. Un code déjà consommé n'a plus d'usage, et le
            // laisser affiché invite à le relire sur un écran resté ouvert.
            .Where(s => s.Status is not (ShipmentStatus.Delivered or ShipmentStatus.Cancelled))
            .Where(s => !string.IsNullOrWhiteSpace(s.DeliveryCode))
            .Select(s => new ShipmentDeliveryCode(s.Id.Value, s.SellerId, s.DeliveryCode!))
            .ToList();

        return Result.Success(codes);
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
