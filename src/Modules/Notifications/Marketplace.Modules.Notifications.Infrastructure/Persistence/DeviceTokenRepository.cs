using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Notifications.Domain.Devices;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence;

internal sealed class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly NotificationsDbContext _dbContext;

    public DeviceTokenRepository(NotificationsDbContext dbContext) => _dbContext = dbContext;

    public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
        => _dbContext.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

    public async Task AddAsync(DeviceToken device, CancellationToken cancellationToken = default)
        => await _dbContext.DeviceTokens.AddAsync(device, cancellationToken);

    public void Remove(DeviceToken device)
        => _dbContext.DeviceTokens.Remove(device);

    public async Task<IReadOnlyList<DeviceToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.DeviceTokens.Where(d => d.UserId == userId).ToListAsync(cancellationToken);

    public async Task RemoveByTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        var rows = await _dbContext.DeviceTokens.Where(d => tokens.Contains(d.Token)).ToListAsync(cancellationToken);
        _dbContext.DeviceTokens.RemoveRange(rows);
    }
}
