using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Application.Couriers;

/// <summary>
/// Le coursier côté dispatch : sa disponibilité, sa position, sa zone.
///
/// SON DROIT DE TRAVAILLER N'EST PAS DÉCIDÉ ICI. Il est recopié depuis la marketplace,
/// qui seule sait s'il est vérifié ou suspendu — voir <see cref="ApplyStandingAsync"/>.
/// Le laisser se décider des deux côtés garantirait qu'ils divergent, et un coursier
/// suspendu continuerait à recevoir des courses.
/// </summary>
public sealed class CourierService
{
    private readonly ICourierRepository _couriers;
    private readonly IDispatchUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CourierService(
        ICourierRepository couriers, IDispatchUnitOfWork unitOfWork, TimeProvider? clock = null)
    {
        _couriers = couriers;
        _unitOfWork = unitOfWork;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Recopie le statut annoncé par la marketplace. Crée le coursier s'il est inconnu :
    /// c'est ainsi qu'il entre dans le service, sans inscription séparée.
    /// </summary>
    public async Task<Result> ApplyStandingAsync(
        Guid courierId, string displayName, CourierStanding standing, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(courierId), cancellationToken);

        if (courier is null)
        {
            var registered = Courier.Register(courierId, displayName, VehicleType.Motorcycle);
            if (registered.IsFailure)
            {
                return registered;
            }

            courier = registered.Value;
            await _couriers.AddAsync(courier, cancellationToken);
        }

        courier.ApplyStanding(standing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> GoOnlineAsync(CourierId courierId, CancellationToken cancellationToken = default)
        => await MutateAsync(courierId, c => c.GoOnline(), cancellationToken);

    public async Task<Result> GoOfflineAsync(CourierId courierId, CancellationToken cancellationToken = default)
        => await MutateAsync(courierId, c => { c.GoOffline(); return Result.Success(); }, cancellationToken);

    public async Task<Result> SetServiceAreaAsync(
        CourierId courierId, IEnumerable<string> communeCodes, CancellationToken cancellationToken = default)
        => await MutateAsync(courierId, c => c.SetServiceArea(communeCodes), cancellationToken);

    /// <summary>
    /// Enregistre la position du coursier.
    ///
    /// C'est l'écriture la plus fréquente du service — toutes les quinze secondes par
    /// coursier en ligne. Elle ne touche que la ligne du coursier, sans agrégat chargé
    /// autour : c'est ce qui la rend soutenable pour une flotte entière.
    /// </summary>
    public async Task<Result> UpdatePositionAsync(
        CourierId courierId, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var point = GeoPoint.Create(latitude, longitude);
        if (point.IsFailure)
        {
            return point;
        }

        return await MutateAsync(
            courierId, c => c.UpdatePosition(point.Value, _clock.GetUtcNow().UtcDateTime), cancellationToken);
    }

    /// <summary>Course terminée ou perdue : le coursier redevient disponible.</summary>
    public async Task<Result> ReleaseAsync(CourierId courierId, CancellationToken cancellationToken = default)
        => await MutateAsync(courierId, c => { c.ReleaseFromDelivery(); return Result.Success(); }, cancellationToken);

    private async Task<Result> MutateAsync(
        CourierId courierId, Func<Courier, Result> mutate, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetByIdAsync(courierId, cancellationToken);
        if (courier is null)
        {
            return Result.Failure(Error.NotFound("courier.not_found", "Coursier introuvable."));
        }

        var mutated = mutate(courier);
        if (mutated.IsFailure)
        {
            return mutated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
