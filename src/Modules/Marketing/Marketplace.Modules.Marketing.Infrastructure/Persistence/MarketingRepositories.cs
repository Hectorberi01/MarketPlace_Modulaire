using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;

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
