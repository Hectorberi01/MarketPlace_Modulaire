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

    /// <summary>
    /// `AsNoTracking` : les deux appelants ne lisent que le jeton, et la purge des
    /// jetons invalides passe par `RemoveByTokensAsync`, qui requête à nouveau en suivi.
    /// Sans cela, une diffusion chargeait dans le même contexte les appareils de tous
    /// ses destinataires, et le moindre enregistrement repassait sur ce graphe entier.
    /// </summary>
    public async Task<IReadOnlyList<DeviceToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.DeviceTokens.AsNoTracking().Where(d => d.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListUserIdsAsync(string? platform, int max, CancellationToken cancellationToken = default)
    {
        var requete = _dbContext.DeviceTokens.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(platform))
        {
            // COMPARAISON DIRECTE, PAS `ToLower()`. `DeviceToken` normalise déjà la
            // plateforme en minuscules à la création comme à la réaffectation : un
            // `lower(platform)` en SQL n'aurait rien corrigé et aurait interdit tout
            // index sur cette colonne.
            var p = platform.Trim().ToLowerInvariant();
            requete = requete.Where(d => d.Platform == p);
        }

        // DISTINCT côté base : un même utilisateur a souvent deux ou trois appareils, et
        // la diffusion doit lui écrire UNE fois. Dédoublonner en mémoire aurait rapatrié
        // tous les jetons pour en jeter la moitié.
        //
        // L'ORDRE EST EXPLICITE, parce que le lot est TRONQUÉ. Sans `OrderBy`,
        // PostgreSQL rend l'ordre qui l'arrange — en pratique le même d'un appel à
        // l'autre : relancer la diffusion aurait retouché exactement les mêmes personnes
        // et jamais les suivantes, pendant que l'écran annonçait « n destinataires ».
        // Un ordre stable ne règle pas la pagination, mais il rend le lot reproductible
        // et le défaut visible.
        return await requete
            .Select(d => d.UserId)
            .Distinct()
            .OrderBy(id => id)
            .Take(max)
            .ToListAsync(cancellationToken);
    }

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
