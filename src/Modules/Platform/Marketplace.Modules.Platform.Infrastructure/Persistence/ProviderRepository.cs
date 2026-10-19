using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Infrastructure.Persistence;

internal sealed class ProviderRepository : IProviderRepository
{
    private readonly PlatformDbContext _context;

    public ProviderRepository(PlatformDbContext context)
    {
        _context = context;
    }

    public Task<Provider?> GetAsync(ProviderId id, CancellationToken cancellationToken = default)
        => _context.Providers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Provider?> FindAsync(ProviderChannel channel, string code, CancellationToken cancellationToken = default)
        => _context.Providers.FirstOrDefaultAsync(
            p => p.Channel == channel && p.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Provider>> ListAsync(
        ProviderChannel? channel = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Providers.AsQueryable();
        if (channel is not null)
        {
            query = query.Where(p => p.Channel == channel.Value);
        }

        return await query
            .OrderBy(p => p.Channel)
            .ThenBy(p => p.Priority)
            .ThenBy(p => p.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Provider>> ListEnabledAsync(
        ProviderChannel channel, CancellationToken cancellationToken = default)
        => await _context.Providers
            .Where(p => p.Channel == channel && p.IsEnabled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Code)
            .ToListAsync(cancellationToken);

    public void Add(Provider provider) => _context.Providers.Add(provider);

    public void Remove(Provider provider) => _context.Providers.Remove(provider);

    public void AppendLog(ProviderChangeLog entry) => _context.ProviderChangeLogs.Add(entry);

    public async Task<IReadOnlyList<ProviderChangeLog>> ListLogAsync(
        int limit, CancellationToken cancellationToken = default)
        => await _context.ProviderChangeLogs
            .OrderByDescending(l => l.AtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
