using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Carriers;

namespace Marketplace.Modules.Shipping.Application.Carriers;

/// <summary>Tous les transporteurs du catalogue (admin).</summary>
public sealed record ListCarriersQuery : IQuery<IReadOnlyList<CarrierSummary>>;

/// <summary>Transporteurs actifs (choix vendeur).</summary>
public sealed record ListActiveCarriersQuery : IQuery<IReadOnlyList<CarrierSummary>>;

/// <summary>Un transporteur par son id.</summary>
public sealed record GetCarrierQuery(Guid CarrierId) : IQuery<CarrierSummary>;

internal sealed class ListCarriersQueryHandler : IQueryHandler<ListCarriersQuery, IReadOnlyList<CarrierSummary>>
{
    private readonly ICarrierRepository _repository;

    public ListCarriersQueryHandler(ICarrierRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CarrierSummary>>> Handle(ListCarriersQuery query, CancellationToken cancellationToken)
    {
        var carriers = await _repository.ListAllAsync(cancellationToken);
        IReadOnlyList<CarrierSummary> summaries = carriers.Select(CarrierMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListActiveCarriersQueryHandler : IQueryHandler<ListActiveCarriersQuery, IReadOnlyList<CarrierSummary>>
{
    private readonly ICarrierRepository _repository;

    public ListActiveCarriersQueryHandler(ICarrierRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CarrierSummary>>> Handle(ListActiveCarriersQuery query, CancellationToken cancellationToken)
    {
        var carriers = await _repository.ListActiveAsync(cancellationToken);
        IReadOnlyList<CarrierSummary> summaries = carriers.Select(CarrierMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetCarrierQueryHandler : IQueryHandler<GetCarrierQuery, CarrierSummary>
{
    private readonly ICarrierRepository _repository;

    public GetCarrierQueryHandler(ICarrierRepository repository) => _repository = repository;

    public async Task<Result<CarrierSummary>> Handle(GetCarrierQuery query, CancellationToken cancellationToken)
    {
        var carrier = await _repository.GetByIdAsync(new CarrierId(query.CarrierId), cancellationToken);
        return carrier is null
            ? Error.NotFound("shipping.carrier.not_found", "Transporteur introuvable.")
            : CarrierMapper.ToSummary(carrier);
    }
}
