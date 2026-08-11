using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Inventory.Domain.Locations;

namespace Marketplace.Modules.Inventory.Application.Locations.Queries;

/// <summary>Liste les lieux d'expédition d'un propriétaire (vendeur), pour alimenter
/// le sélecteur « expédié depuis » à la création d'une offre.</summary>
public sealed record ListFulfillmentLocationsQuery(Guid OwnerId) : IQuery<IReadOnlyList<FulfillmentLocationSummary>>;

/// <summary>Tous les lieux d'expédition de la plateforme (back-office admin).</summary>
public sealed record ListAllFulfillmentLocationsQuery : IQuery<IReadOnlyList<FulfillmentLocationSummary>>;

internal sealed class ListAllFulfillmentLocationsQueryHandler
    : IQueryHandler<ListAllFulfillmentLocationsQuery, IReadOnlyList<FulfillmentLocationSummary>>
{
    private readonly IFulfillmentLocationRepository _repository;

    public ListAllFulfillmentLocationsQueryHandler(IFulfillmentLocationRepository repository)
        => _repository = repository;

    public async Task<Result<IReadOnlyList<FulfillmentLocationSummary>>> Handle(
        ListAllFulfillmentLocationsQuery query, CancellationToken cancellationToken)
    {
        var locations = await _repository.ListAllAsync(500, cancellationToken);
        IReadOnlyList<FulfillmentLocationSummary> summaries = locations
            // ⚠️ Latitude/Longitude sont RELUES, plus écrasées par « null, null ».
            // Le value object les validait, le BFF les acceptait, et cette projection les
            // jetait : une saisie GPS ne survivait pas à sa propre relecture.
            .Select(l => new FulfillmentLocationSummary(
                l.Id.Value, l.Type.ToString(), l.OwnerId,
                l.Address.CommuneCode, l.Address.CommuneName, l.Address.Quartier,
                l.Address.Landmark, l.Address.Line, l.Address.CountryCode,
                l.Address.Latitude, l.Address.Longitude, l.Address.ContactPhone))
            .ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListFulfillmentLocationsQueryHandler
    : IQueryHandler<ListFulfillmentLocationsQuery, IReadOnlyList<FulfillmentLocationSummary>>
{
    private readonly IFulfillmentLocationRepository _repository;

    public ListFulfillmentLocationsQueryHandler(IFulfillmentLocationRepository repository)
        => _repository = repository;

    public async Task<Result<IReadOnlyList<FulfillmentLocationSummary>>> Handle(
        ListFulfillmentLocationsQuery query, CancellationToken cancellationToken)
    {
        var locations = await _repository.ListByOwnerAsync(query.OwnerId, cancellationToken);
        IReadOnlyList<FulfillmentLocationSummary> summaries = locations
            // ⚠️ Latitude/Longitude sont RELUES, plus écrasées par « null, null ».
            // Le value object les validait, le BFF les acceptait, et cette projection les
            // jetait : une saisie GPS ne survivait pas à sa propre relecture.
            .Select(l => new FulfillmentLocationSummary(
                l.Id.Value, l.Type.ToString(), l.OwnerId,
                l.Address.CommuneCode, l.Address.CommuneName, l.Address.Quartier,
                l.Address.Landmark, l.Address.Line, l.Address.CountryCode,
                l.Address.Latitude, l.Address.Longitude, l.Address.ContactPhone))
            .ToList();
        return Result.Success(summaries);
    }
}
