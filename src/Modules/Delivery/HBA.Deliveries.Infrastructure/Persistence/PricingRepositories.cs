using HBA.Deliveries.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

internal sealed class DeliveryZoneRepository : IDeliveryZoneRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public DeliveryZoneRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<DeliveryZone>> ListActiveAsync(CancellationToken cancellationToken = default)
        // AsNoTracking : ces zones ne servent qu'à calculer. Les suivre ferait
        // grossir le change tracker à chaque devis, sur un DbContext qui écrit
        // par ailleurs le devis lui-même.
        => await _dbContext.DeliveryZones.AsNoTracking()
            .Where(z => z.IsActive)
            .OrderByDescending(z => z.Priority)
            .ToListAsync(cancellationToken);

    public async Task<DeliveryZone?> GetByIdAsync(DeliveryZoneId id, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id, cancellationToken);

    public async Task AddAsync(DeliveryZone zone, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryZones.AddAsync(zone, cancellationToken);
}

internal sealed class PricingRuleRepository : IPricingRuleRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public PricingRuleRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<PricingRule>> ListActiveAsync(
        DateTime atUtc, CancellationToken cancellationToken = default)
        // Les paliers de poids sont chargés AVEC la grille : le calcul les
        // consulte systématiquement, et un chargement paresseux produirait une
        // requête par grille sur le chemin du devis.
        => await _dbContext.PricingRules.AsNoTracking()
            .Include(r => r.WeightTiers)
            .Where(r => r.IsActive
                        && r.ValidFromUtc <= atUtc
                        && (r.ValidUntilUtc == null || r.ValidUntilUtc > atUtc))
            .ToListAsync(cancellationToken);

    public async Task<PricingRule?> GetByIdAsync(PricingRuleId id, CancellationToken cancellationToken = default)
        => await _dbContext.PricingRules
            .Include(r => r.WeightTiers)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddAsync(PricingRule rule, CancellationToken cancellationToken = default)
        => await _dbContext.PricingRules.AddAsync(rule, cancellationToken);
}

internal sealed class DeliveryQuoteRepository : IDeliveryQuoteRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public DeliveryQuoteRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<DeliveryQuote?> GetByIdAsync(
        DeliveryQuoteId id, CancellationToken cancellationToken = default)
        // SUIVI, contrairement aux deux autres : ce devis va être consommé, donc
        // modifié. AsNoTracking ferait échouer l'enregistrement en silence.
        => await _dbContext.DeliveryQuotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task AddAsync(DeliveryQuote quote, CancellationToken cancellationToken = default)
        => await _dbContext.DeliveryQuotes.AddAsync(quote, cancellationToken);
}
