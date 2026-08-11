using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Contracts;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Public;

/// <summary>
/// Implémentation en processus de <see cref="IDeliveryModuleApi"/>.
///
/// Lectures uniquement, et toutes en <c>AsNoTracking</c> : rien de ce qui sort
/// d'ici ne sera modifié, et suivre ces entités ferait grossir le change tracker
/// d'un DbContext partagé avec des écritures.
/// </summary>
internal sealed class DeliveryModuleApi : IDeliveryModuleApi
{
    private readonly DeliveriesDbContext _dbContext;
    private readonly IDriverLocationCache _locations;

    public DeliveryModuleApi(DeliveriesDbContext dbContext, IDriverLocationCache locations)
    {
        _dbContext = dbContext;
        _locations = locations;
    }

    public async Task<DeliverySummary?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var id = new DeliveryId(deliveryId);
        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return delivery is null ? null : await ToSummaryAsync(delivery, cancellationToken);
    }

    public async Task<DeliverySummary?> GetByReferenceAsync(
        string reference, string source, CancellationToken cancellationToken = default)
    {
        // La source arrive en TEXTE : c'est le contrat public, et un appelant
        // externe n'a pas à connaître nos valeurs numériques. Une source
        // inconnue n'est pas une erreur — c'est simplement « aucune course ».
        if (!Enum.TryParse<DeliverySource>(source, ignoreCase: true, out var parsed))
        {
            return null;
        }

        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Reference == reference && d.Source == parsed, cancellationToken);

        return delivery is null ? null : await ToSummaryAsync(delivery, cancellationToken);
    }

    public async Task<DeliveryTracking?> GetTrackingAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var id = new DeliveryId(deliveryId);
        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (delivery is null)
        {
            return null;
        }

        var driver = await LoadDriverAsync(delivery, cancellationToken);

        // ─────────────────────────────────────────────────────────────────────
        // LA POSITION N'EST EXPOSÉE QUE PENDANT LE TRANSPORT.
        //
        // Ni avant l'acceptation — il n'y a personne à suivre —, ni après la
        // remise. Ce n'est pas une optimisation : continuer à diffuser la
        // position d'une personne en dehors de la mission qui la justifie serait
        // une collecte sans finalité, et elle ne se verrait nulle part puisque
        // l'écran de suivi resterait ouvert sans que personne s'en aperçoive.
        // ─────────────────────────────────────────────────────────────────────
        var inTransit = delivery.Status is DeliveryStatus.DriverAccepted
            or DeliveryStatus.ArrivedAtPickup
            or DeliveryStatus.PickedUp
            or DeliveryStatus.InTransit
            or DeliveryStatus.ArrivedAtDropoff;

        DriverPosition? position = null;
        if (inTransit && delivery.AssignedDriverId is { } assigned)
        {
            position = await _locations.GetAsync(assigned, cancellationToken);
        }

        return new DeliveryTracking(
            delivery.Id.Value,
            delivery.Status.ToString(),
            position?.Position.Latitude,
            position?.Position.Longitude,
            position?.ReportedAtUtc,
            driver?.FullName,
            driver?.Phone);
    }

    private async Task<DeliverySummary> ToSummaryAsync(
        Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken)
    {
        var driver = await LoadDriverAsync(delivery, cancellationToken);

        return new DeliverySummary(
            delivery.Id.Value,
            delivery.Reference,
            delivery.Source.ToString(),
            delivery.Type.ToString(),
            delivery.Status.ToString(),
            delivery.Pickup.Summary,
            delivery.Dropoff.Summary,
            driver?.FullName,
            driver?.Phone,
            delivery.CreatedAtUtc,
            delivery.AcceptedAtUtc,
            delivery.PickedUpAtUtc,
            delivery.DeliveredAtUtc);
    }

    private async Task<Domain.Drivers.Driver?> LoadDriverAsync(
        Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.AssignedDriverId is not { } assigned)
        {
            return null;
        }

        return await _dbContext.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == assigned, cancellationToken);
    }
}
