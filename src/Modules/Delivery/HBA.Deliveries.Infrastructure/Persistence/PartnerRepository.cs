using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Partners;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

internal sealed class PartnerRepository : IPartnerRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public PartnerRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Partner?> GetByIdAsync(PartnerId id, CancellationToken cancellationToken = default)
        => await _dbContext.Partners
            .Include(p => p.ApiKeys)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Partner?> FindByApiKeyPrefixAsync(
        string prefix, CancellationToken cancellationToken = default)
        // ─────────────────────────────────────────────────────────────────────
        // LA REQUÊTE DU CHEMIN D'AUTHENTIFICATION.
        //
        // Elle s'exécute à chaque appel partenaire et touche l'index unique sur
        // le préfixe. Le partenaire est chargé AVEC ses clés : la vérification du
        // condensat se fait ensuite en mémoire, à temps constant.
        //
        // SUIVI, et non AsNoTracking : l'appelant met à jour LastUsedAtUtc — une
        // fois par heure au plus, mais il le fait. Détacher l'entité rendrait
        // cette écriture silencieusement sans effet.
        // ─────────────────────────────────────────────────────────────────────
        => await _dbContext.Partners
            .Include(p => p.ApiKeys)
            .FirstOrDefaultAsync(
                p => p.ApiKeys.Any(k => k.Prefix == prefix && k.RevokedAtUtc == null),
                cancellationToken);

    public async Task<IReadOnlyList<Partner>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Partners
            .AsNoTracking()
            .Include(p => p.ApiKeys)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<int> CountDeliveriesTodayAsync(
        PartnerId id, CancellationToken cancellationToken = default)
    {
        // Journée en UTC, et c'est un choix à connaître : le Bénin est à UTC+1,
        // donc un quota « quotidien » se réinitialise à 1 h du matin locale. C'est
        // acceptable pour un plafond anti-abus ; cela ne le serait pas pour de la
        // facturation, qui devra raisonner en heure locale.
        var since = DateTime.UtcNow.Date;

        return await _dbContext.Deliveries
            .AsNoTracking()
            .CountAsync(d => d.PartnerId == id.Value && d.CreatedAtUtc >= since, cancellationToken);
    }

    public async Task AddAsync(Partner partner, CancellationToken cancellationToken = default)
        => await _dbContext.Partners.AddAsync(partner, cancellationToken);
}
