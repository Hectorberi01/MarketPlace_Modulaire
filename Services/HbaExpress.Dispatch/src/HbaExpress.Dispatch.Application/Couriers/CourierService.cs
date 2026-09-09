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
    ///
    /// UNE ANNONCE PÉRIMÉE EST UN SUCCÈS, PAS UNE ERREUR. Elle a été correctement reçue
    /// et délibérément écartée — voir Courier.ApplyStanding. Répondre en échec ferait
    /// réessayer l'outbox de la marketplace en boucle jusqu'à la lettre morte, pour un
    /// message qu'on ne veut de toute façon pas appliquer.
    /// </summary>
    /// <param name="announcedAtUtc">Instant d'ÉMISSION de l'annonce par la marketplace.</param>
    public async Task<Result> ApplyStandingAsync(
        Guid courierId, Guid userId, string displayName, CourierStanding standing,
        DateTime announcedAtUtc, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(courierId), cancellationToken);

        if (courier is null)
        {
            // Le UserId est indispensable À LA CRÉATION : c'est lui qui permettra au
            // coursier de s'authentifier. Un coursier créé sans compte serait offert des
            // courses et incapable d'en prendre une.
            var registered = Courier.Register(courierId, userId, displayName, VehicleType.Motorcycle);
            if (registered.IsFailure)
            {
                return registered;
            }

            courier = registered.Value;
            await _couriers.AddAsync(courier, cancellationToken);
        }

        if (!courier.ApplyStanding(standing, announcedAtUtc))
        {
            // Rien à écrire : SaveChanges est sauté, pas seulement inutile. Un coursier
            // tout juste créé au-dessus n'entre pas ici — sa première annonce passe
            // toujours, StandingAnnouncedAtUtc étant nul.
            return Result.Success();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Le coursier derrière un compte utilisateur, ou <c>null</c> s'il est inconnu.
    ///
    /// Le jeton JWT du coursier porte son COMPTE (claim « sub »), pas son identifiant de
    /// coursier : la marketplace n'émet qu'un seul type de jeton, pour tous ses publics.
    /// Cette résolution est donc le premier geste de chaque route de l'application
    /// coursier.
    /// </summary>
    public async Task<CourierId?> ResolveByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var courier = await _couriers.GetByUserAsync(userId, cancellationToken);
        return courier?.Id;
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
