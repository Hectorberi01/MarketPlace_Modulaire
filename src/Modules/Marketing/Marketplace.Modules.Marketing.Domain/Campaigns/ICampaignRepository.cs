namespace Marketplace.Modules.Marketing.Domain.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken = default);

    void Remove(Campaign campaign);

    Task<Campaign?> GetByIdAsync(CampaignId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken cancellationToken = default);
}
