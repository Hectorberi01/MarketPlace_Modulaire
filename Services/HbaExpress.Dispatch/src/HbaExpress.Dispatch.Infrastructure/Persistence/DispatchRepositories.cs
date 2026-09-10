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

    /// <summary>
    /// La flotte présente. `AsNoTracking` : lecture pure, rien n'est muté ensuite.
    ///
    /// Le filtre porte sur (Standing, Availability), exactement le couple que l'index
    /// de `couriers` couvre déjà — celui posé pour l'appariement sert ici sans rien
    /// ajouter.
    ///
    /// La POSITION NULLE est écartée en base plutôt qu'à l'affichage : un coursier
    /// marqué en ligne qui n'a jamais émis de position n'a rien à faire sur une carte,
    /// et le laisser remonter obligerait chaque appelant à refaire ce test.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// LE TEST PORTE SUR L'HORODATAGE, ET NON SUR `LastPosition`.
    ///
    /// `LastPosition` est un type possédé en PARTAGE DE TABLE dont les deux colonnes
    /// sont nullables. EF Core ne peut donc pas distinguer « pas de position » de
    /// « position dont tout est nul » : c'est le cas qu'il signale par
    /// `OptionalDependentWithoutIdentifyingPropertyWarning`, et la traduction du
    /// `!= null` y tient d'une heuristique plutôt que du modèle.
    ///
    /// `LastPositionAtUtc` est un scalaire, sans ambiguïté aucune, et `UpdatePosition`
    /// écrit toujours les deux ensemble : le filtre est le même, il est simplement
    /// exprimé sur ce qui se traduit sans discussion.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    public async Task<IReadOnlyList<Courier>> ListOnFieldAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Couriers
            .AsNoTracking()
            .Where(c => c.Standing == CourierStanding.Active
                && (c.Availability == CourierAvailability.Online || c.Availability == CourierAvailability.OnDelivery)
                && c.LastPositionAtUtc != null)
            // Départage les homonymes : sans lui, deux coursiers du même nom s'ordonnent
            // au gré du plan d'exécution, et la liste sautille d'un relevé à l'autre.
            //
            // `EF.Property<Guid>` PLUTÔT QUE `c.Id`. Les deux se traduisent en la même
            // colonne, mais `CourierId` n'implémente pas `IComparable` : le jour où cette
            // requête basculerait en évaluation cliente — fournisseur en mémoire dans un
            // test, filtre non traduisible ajouté en amont — `.ThenBy(c => c.Id)`
            // lèverait à l'exécution. Sous cette forme, elle échoue franchement au lieu
            // de retomber en silence sur une comparaison impossible.
            .OrderBy(c => c.DisplayName)
            .ThenBy(c => EF.Property<Guid>(c, "Id"))
            .ToListAsync(cancellationToken);
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

    /// <summary>
    /// Réserve les courses à ré-offrir, en sautant celles qu'une autre instance traite.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// DEUX TEMPS, PARCE QU'ON NE PEUT PAS FAIRE AUTREMENT.
    ///
    /// 1. Du SQL brut réserve les IDENTIFIANTS avec « FOR UPDATE SKIP LOCKED ».
    /// 2. Une requête EF ordinaire charge les agrégats complets pour ces identifiants.
    ///
    /// Pourquoi pas en une fois : `FromSqlRaw` suivi d'un `.Include(...)` est une
    /// COMPOSITION, et EF enveloppe alors le SQL dans une sous-requête — où PostgreSQL
    /// refuse « FOR UPDATE ». Et un « FOR UPDATE » posé sur une requête jointe
    /// verrouillerait aussi les offres et les ramassages, ce qu'on ne demande pas.
    ///
    /// Les verrous posés au temps 1 tiennent jusqu'au COMMIT — c'est-à-dire pendant tout
    /// le temps 2 et tout le traitement qui suit, à condition que l'appelant ait ouvert
    /// une transaction. Voir IDispatchUnitOfWork.InTransactionAsync : sans elle, ce code
    /// a l'air correct et ne protège rien.
    ///
    /// SANS SKIP LOCKED, une seconde instance ATTENDRAIT les lignes de la première au
    /// lieu de passer à côté : on aurait échangé le double dispatch contre une file
    /// d'attente. Avec, les deux instances se partagent le travail.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// SUIVI EF ACTIVÉ au temps 2 : le balayage mute ces courses (ré-offre ou expiration).
    /// </summary>
    public async Task<IReadOnlyList<DeliveryJob>> ListAwaitingRedispatchAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var limit = take <= 0 ? 50 : take;

        // « Created » AUTANT QUE « Offered » : une course qui n'a jamais trouvé de
        // coursier n'a pas d'offre en cours, et resterait donc invisible d'une requête
        // qui ne regarde que les courses offertes.
        //
        // Le statut est stocké en CHAÎNE (HasConversion<string>) : la comparaison se fait
        // donc sur des littéraux, pas sur des entiers.
        const string sql =
            """
            SELECT j."Id"
            FROM "dispatch"."delivery_jobs" j
            WHERE j."Status" IN ('Created', 'Offered')
              AND NOT EXISTS (
                    SELECT 1 FROM "dispatch"."dispatch_offers" o
                    WHERE o."DeliveryJobId" = j."Id"
                      AND o."Status" = 'Pending'
                      AND o."ExpiresAtUtc" > {0})
            ORDER BY j."CreatedAtUtc"
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

        var claimed = await _dbContext.Database
            .SqlQueryRaw<Guid>(sql, nowUtc, limit)
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return Array.Empty<DeliveryJob>();
        }

        var ids = claimed.Select(id => new DeliveryJobId(id)).ToList();

        return await _dbContext.DeliveryJobs
            .Include(j => j.Offers)
            .Include(j => j.Pickups)
            .Where(j => ids.Contains(j.Id))
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

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
