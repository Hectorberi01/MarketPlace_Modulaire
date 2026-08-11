using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

internal sealed class DeliveryRepository : IDeliveryRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public DeliveryRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Domain.Deliveries.Delivery?> GetByIdAsync(DeliveryId id, CancellationToken cancellationToken = default)
        // Les propositions sont chargées AVEC la course : le dispatch et
        // l'acceptation les consultent toutes deux, et un chargement paresseux
        // produirait une requête supplémentaire sur le chemin le plus sensible.
        => await _dbContext.Deliveries
            .Include(d => d.Assignments)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<Domain.Deliveries.Delivery?> GetByReferenceAsync(
        string reference, DeliverySource source, CancellationToken cancellationToken = default)
        => await _dbContext.Deliveries
            .Include(d => d.Assignments)
            .FirstOrDefaultAsync(d => d.Reference == reference && d.Source == source, cancellationToken);

    public async Task<IReadOnlyList<Domain.Deliveries.Delivery>> ListAwaitingDriverAsync(
        int take = 50, CancellationToken cancellationToken = default)
        // Les plus anciennes d'abord : une course qui attend depuis dix minutes
        // passe avant celle créée à l'instant. Sans cet ordre, une course
        // difficile à pourvoir peut être indéfiniment doublée par des courses
        // plus faciles — et le client, lui, attend toujours.
        => await _dbContext.Deliveries
            .Include(d => d.Assignments)
            .Where(d => d.Status == DeliveryStatus.SearchingDriver
                        || d.Status == DeliveryStatus.NoDriverAvailable)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Domain.Deliveries.Delivery>> ListScheduledDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default)
        // ─────────────────────────────────────────────────────────────────────
        // LE DÉLAI D'ANTICIPATION EST APPLIQUÉ ICI, PAS EN MÉMOIRE.
        //
        // Filtrer sur « ScheduledForUtc <= maintenant + délai » se traduit en SQL
        // et touche l'index partiel. Charger toutes les courses programmées pour
        // les trier ensuite ramènerait une semaine de créneaux à chaque tour de
        // boucle — soit toutes les cinq secondes.
        // ─────────────────────────────────────────────────────────────────────
        => await _dbContext.Deliveries
            .Include(d => d.Assignments)
            .Where(d => d.Status == DeliveryStatus.Pending
                        && d.ScheduledForUtc != null
                        && d.ScheduledForUtc <= nowUtc + Domain.Deliveries.Delivery.ScheduledDispatchLeadTime)
            .OrderBy(d => d.ScheduledForUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Domain.Deliveries.Delivery>> ListStaleOffersAsync(
        TimeSpan offerTimeout, int take = 50, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow - offerTimeout;

        // Le filtre porte sur la PROPOSITION EN COURS, pas sur la course : une
        // course peut avoir été proposée trois fois, seule la dernière compte.
        // `Any` se traduit en EXISTS ; l'index sur delivery_assignments.DriverId
        // ne sert pas ici, mais le volume de courses en attente reste petit —
        // c'est tout l'objet de l'index partiel sur `deliveries`.
        return await _dbContext.Deliveries
            .Include(d => d.Assignments)
            .Where(d => d.Status == DeliveryStatus.DriverAssigned
                        && d.Assignments.Any(a => a.Outcome == AssignmentOutcome.Offered
                                                  && a.OfferedAtUtc <= deadline))
            .OrderBy(d => d.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken = default)
        => await _dbContext.Deliveries.AddAsync(delivery, cancellationToken);
}

internal sealed class DriverRepository : IDriverRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public DriverRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Driver?> GetByIdAsync(DriverId id, CancellationToken cancellationToken = default)
        => await _dbContext.Drivers.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Drivers.FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Driver>> ListByIdsAsync(
        IReadOnlyCollection<DriverId> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            // Sans ce raccourci, EF produit « WHERE Id IN () », que PostgreSQL
            // refuse. Le cas se présente à chaque dispatch dans une zone sans
            // livreur en ligne — c'est-à-dire souvent.
            return Array.Empty<Driver>();
        }

        // ⚠️ On filtre sur `d.Id`, PAS sur `d.Id.Value`.
        //
        // `Id` porte un convertisseur de valeur (DriverId ↔ Guid). EF sait
        // traduire `ids.Contains(d.Id)` en appliquant le convertisseur aux
        // paramètres ; il ne sait PAS traduire un accès au champ `.Value` d'un
        // type converti — il échouerait à l'exécution, pas à la compilation.
        var keys = ids as IReadOnlyList<DriverId> ?? ids.ToList();

        return await _dbContext.Drivers
            .Where(d => keys.Contains(d.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Driver driver, CancellationToken cancellationToken = default)
        => await _dbContext.Drivers.AddAsync(driver, cancellationToken);
}
