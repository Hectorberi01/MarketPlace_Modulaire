using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Outbound;
using Microsoft.EntityFrameworkCore;

namespace HbaExpress.Dispatch.Infrastructure.Persistence;

internal sealed class CourierRepository : ICourierRepository
{
    private readonly DispatchDbContext _dbContext;

    public CourierRepository(DispatchDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Courier courier, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.AddAsync(courier, cancellationToken);

    public async Task<Courier?> GetByIdAsync(CourierId id, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    // AVEC SUIVI EF, comme la lecture par identifiant : c'est par ici que passent toutes
    // les routes de l'application coursier, y compris celles qui mutent (mise en ligne,
    // position, zone). Sans suivi, aucune de ces écritures ne serait enregistrée.
    public async Task<Courier?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Couriers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    /// <summary>
    /// Candidats à une offre.
    ///
    /// LE FILTRE GROSSIER EST FAIT EN BASE, L'AFFINAGE EN MÉMOIRE — et le partage est
    /// délibéré. La base sait dire « actif et en ligne » avec un index ; elle ne sait pas
    /// mesurer une distance à vol d'oiseau sans extension géographique, et la zone se
    /// range dans un tableau qu'on ne veut pas indexer pour si peu.
    ///
    /// Le volume rend le choix sûr : une flotte urbaine en ligne, c'est quelques
    /// centaines de coursiers, pas des millions. Filtrer commune et rayon en mémoire
    /// laisse CourierMatcher — une fonction pure — porter toute la règle, là où elle se
    /// teste sans base de données. C'est le point à revoir si la flotte change d'ordre de
    /// grandeur, pas avant.
    /// </summary>
    public async Task<IReadOnlyList<Courier>> ListCandidatesAsync(
        string communeCode, CancellationToken cancellationToken = default)
    {
        var online = await _dbContext.Couriers
            .AsNoTracking()
            .Where(c => c.Standing == CourierStanding.Active && c.Availability == CourierAvailability.Online)
            .ToListAsync(cancellationToken);

        return online.Where(c => c.Serves(communeCode)).ToList();
    }
}

internal sealed class DeliveryJobRepository : IDeliveryJobRepository
{
    private readonly DispatchDbContext _dbContext;

    public DeliveryJobRepository(DispatchDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(DeliveryJob job, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs.AddAsync(job, cancellationToken);

    /// <summary>
    /// Charge une course AVEC ses offres et ses ramassages, et AVEC suivi EF.
    ///
    /// Les deux comptent. Sans les offres, Accept ne trouve jamais l'offre du coursier et
    /// refuse tout le monde. Sans le suivi, EF n'émet aucun UPDATE — donc aucune
    /// évaluation du jeton de concurrence, donc plus de verrou du tout.
    /// </summary>
    public async Task<DeliveryJob?> GetByIdAsync(DeliveryJobId id, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .ThenInclude(p => p.Parcels)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<DeliveryJob?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .ThenInclude(p => p.Parcels)
            .FirstOrDefaultAsync(j => j.OrderId == orderId, cancellationToken);

    // SUIVI ACTIVÉ : le balayage de redispatch mute ces courses (ré-offre ou expiration).
    public async Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            // « Created » AUTANT QUE « Offered » : une course qui n'a jamais trouvé de
            // coursier n'a pas d'offre en cours, et resterait donc invisible d'une
            // requête qui ne regarde que les courses offertes.
            .Where(j => (j.Status == DeliveryJobStatus.Created || j.Status == DeliveryJobStatus.Offered)
                        && !j.Offers.Any(o => o.Status == DispatchOfferStatus.Pending && o.ExpiresAtUtc > nowUtc))
            .OrderBy(j => j.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// LECTURE SEULE, SANS SUIVI EF. L'application coursier interroge cette route à
    /// chaque ouverture d'écran : suivre les entités renvoyées remplirait le
    /// ChangeTracker pour rien, et rien ici n'est muté.
    /// </summary>
    public async Task<IReadOnlyList<DeliveryJob>> ListOfferedToAsync(
        CourierId courierId, DateTime nowUtc, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs
            .AsNoTracking()
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .ThenInclude(p => p.Parcels)
            .Where(j => j.Status == DeliveryJobStatus.Offered
                        && j.Offers.Any(o => o.CourierId == courierId
                                             && o.Status == DispatchOfferStatus.Pending
                                             && o.ExpiresAtUtc > nowUtc))
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<DeliveryJob?> GetActiveForCourierAsync(
        CourierId courierId, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryJobs
            .AsNoTracking()
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .ThenInclude(p => p.Parcels)
            .Where(j => j.AssignedCourierId == courierId
                        && (j.Status == DeliveryJobStatus.Accepted
                            || j.Status == DeliveryJobStatus.PickingUp
                            || j.Status == DeliveryJobStatus.InTransit))
            .OrderByDescending(j => j.AcceptedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}

internal sealed class OutboundMessageRepository : IOutboundMessageRepository
{
    private readonly DispatchDbContext _dbContext;

    public OutboundMessageRepository(DispatchDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(OutboundMessage message, CancellationToken cancellationToken = default)
        => await _dbContext.OutboundMessages.AddAsync(message, cancellationToken);

    // SUIVI ACTIVÉ : le drain marque ces messages envoyés ou en échec. Sans suivi, EF
    // n'émettrait aucun UPDATE et chaque message serait renvoyé à chaque passage.
    public async Task<IReadOnlyList<OutboundMessage>> ListPendingAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default)
        => await _dbContext.OutboundMessages
            .Where(m => m.SentAtUtc == null
                        && m.DeadLetteredAtUtc == null
                        && m.NextAttemptAtUtc <= nowUtc)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken = default)
        => await _dbContext.OutboundMessages
            .AsNoTracking()
            .CountAsync(m => m.DeadLetteredAtUtc != null, cancellationToken);
}
