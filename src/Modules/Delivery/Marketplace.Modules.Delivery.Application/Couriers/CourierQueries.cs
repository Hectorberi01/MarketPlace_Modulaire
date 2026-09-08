using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Application.Couriers;

/// <summary>Un coursier par son identifiant.</summary>
public sealed record GetCourierQuery(Guid CourierId) : IQuery<CourierSummary>;

/// <summary>Le coursier rattaché à un compte utilisateur.</summary>
public sealed record GetCourierByUserQuery(Guid UserId) : IQuery<CourierSummary>;

/// <summary>Liste des coursiers, filtrable par statut (console admin).</summary>
public sealed record ListCouriersQuery(string? Status = null) : IQuery<IReadOnlyList<CourierSummary>>;

internal sealed class GetCourierQueryHandler : IQueryHandler<GetCourierQuery, CourierSummary>
{
    private readonly ICourierRepository _couriers;

    public GetCourierQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<CourierSummary>> Handle(GetCourierQuery query, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(query.CourierId), cancellationToken);
        return courier is null
            ? Error.NotFound("delivery.courier.not_found", "Coursier introuvable.")
            : CourierMapper.ToSummary(courier);
    }
}

internal sealed class GetCourierByUserQueryHandler : IQueryHandler<GetCourierByUserQuery, CourierSummary>
{
    private readonly ICourierRepository _couriers;

    public GetCourierByUserQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<CourierSummary>> Handle(GetCourierByUserQuery query, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetByUserAsync(query.UserId, cancellationToken);
        return courier is null
            ? Error.NotFound("delivery.courier.not_found", "Aucun compte coursier pour cet utilisateur.")
            : CourierMapper.ToSummary(courier);
    }
}

internal sealed class ListCouriersQueryHandler : IQueryHandler<ListCouriersQuery, IReadOnlyList<CourierSummary>>
{
    private readonly ICourierRepository _couriers;

    public ListCouriersQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<IReadOnlyList<CourierSummary>>> Handle(
        ListCouriersQuery query, CancellationToken cancellationToken)
    {
        // Un statut inconnu ne filtre RIEN plutôt que de ne rien rendre : une faute de
        // frappe dans l'URL doit donner une liste complète, pas une page vide qu'on prend
        // pour « aucun coursier ».
        CourierStatus? status = Enum.TryParse<CourierStatus>(query.Status, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        var couriers = await _couriers.ListAsync(status, cancellationToken);
        IReadOnlyList<CourierSummary> views = couriers.Select(CourierMapper.ToSummary).ToList();
        return Result.Success(views);
    }
}
