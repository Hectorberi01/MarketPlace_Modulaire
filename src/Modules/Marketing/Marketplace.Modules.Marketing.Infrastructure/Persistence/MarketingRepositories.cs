using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;
using Marketplace.Modules.Marketing.Domain.Legal;

namespace Marketplace.Modules.Marketing.Infrastructure.Persistence;

internal sealed class CampaignRepository : ICampaignRepository
{
    private readonly MarketingDbContext _dbContext;

    public CampaignRepository(MarketingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Campaign campaign, CancellationToken cancellationToken = default)
        => await _dbContext.Campaigns.AddAsync(campaign, cancellationToken);

    public void Remove(Campaign campaign) => _dbContext.Campaigns.Remove(campaign);

    public async Task<Campaign?> GetByIdAsync(CampaignId id, CancellationToken cancellationToken = default)
        => await _dbContext.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Campaigns.OrderByDescending(c => c.StartUtc).ToListAsync(cancellationToken);
}

internal sealed class ContentBlockRepository : IContentBlockRepository
{
    private readonly MarketingDbContext _dbContext;

    public ContentBlockRepository(MarketingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ContentBlock block, CancellationToken cancellationToken = default)
        => await _dbContext.ContentBlocks.AddAsync(block, cancellationToken);

    public void Remove(ContentBlock block) => _dbContext.ContentBlocks.Remove(block);

    public async Task<ContentBlock?> GetByIdAsync(ContentBlockId id, CancellationToken cancellationToken = default)
        => await _dbContext.ContentBlocks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContentBlock>> ListPublishedBySlotAsync(string slot, CancellationToken cancellationToken = default)
        => await _dbContext.ContentBlocks
            .AsNoTracking()
            .Where(b => b.Slot == slot && b.Status == ContentStatus.Published)
            .OrderBy(b => b.Position)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContentBlock>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.ContentBlocks
            .AsNoTracking()
            .OrderBy(b => b.Slot).ThenBy(b => b.Position)
            .ToListAsync(cancellationToken);
}

internal sealed class LegalDocumentRepository : ILegalDocumentRepository
{
    private readonly MarketingDbContext _dbContext;

    public LegalDocumentRepository(MarketingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(LegalDocument document, CancellationToken cancellationToken = default)
        => await _dbContext.LegalDocuments.AddAsync(document, cancellationToken);

    // ═════════════════════════════════════════════════════════════════════════════════
    // TOUJOURS `Include` LES VERSIONS — SANS EXCEPTION.
    //
    // Les trois invariants de l'agrégat (un seul brouillon, un numéro jamais réemployé,
    // une publication qui ne recule pas) se défendent en REGARDANT LA COLLECTION. Une
    // lecture sans les versions la présenterait vide : le document accepterait alors un
    // second brouillon, et un numéro déjà publié passerait sans un mot.
    //
    // C'est pourquoi aucune de ces méthodes n'offre de variante « légère ». La tentation
    // viendra le jour où la liste de la console paraîtra lente ; la réponse sera de
    // projeter dans la requête, pas de charger un agrégat mutilé.
    // ═════════════════════════════════════════════════════════════════════════════════
    public async Task<LegalDocument?> GetByIdAsync(LegalDocumentId id, CancellationToken cancellationToken = default)
        => await _dbContext.LegalDocuments
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<LegalDocument?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => await _dbContext.LegalDocuments
            .AsNoTracking()
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<LegalDocument>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.LegalDocuments
            .AsNoTracking()
            .Include(d => d.Versions)
            .OrderBy(d => d.Kind)
            .ToListAsync(cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => await _dbContext.LegalDocuments.AnyAsync(d => d.Slug == slug, cancellationToken);
}

internal sealed class LegalAcceptanceRepository : ILegalAcceptanceRepository
{
    private readonly MarketingDbContext _dbContext;

    public LegalAcceptanceRepository(MarketingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(LegalAcceptance acceptance, CancellationToken cancellationToken = default)
        => await _dbContext.LegalAcceptances.AddAsync(acceptance, cancellationToken);

    public async Task<LegalAcceptance?> FindAsync(
        Guid userId, LegalDocumentVersionId versionId, CancellationToken cancellationToken = default)
        => await _dbContext.LegalAcceptances
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.VersionId == versionId, cancellationToken);

    public async Task<IReadOnlyList<LegalAcceptance>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.LegalAcceptances
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AcceptedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LegalAcceptance>> ListByVersionAsync(
        LegalDocumentVersionId versionId, CancellationToken cancellationToken = default)
        => await _dbContext.LegalAcceptances
            .AsNoTracking()
            .Where(a => a.VersionId == versionId)
            .OrderByDescending(a => a.AcceptedOnUtc)
            .ToListAsync(cancellationToken);
}
