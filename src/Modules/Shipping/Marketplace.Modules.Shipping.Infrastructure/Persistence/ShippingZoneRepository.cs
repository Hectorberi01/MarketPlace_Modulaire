using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Shipping.Domain.Zones;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence;

internal sealed class ShippingZoneRepository : IShippingZoneRepository
{
    private readonly ShippingDbContext _dbContext;

    public ShippingZoneRepository(ShippingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ShippingZone zone, CancellationToken cancellationToken = default)
        => await _dbContext.ShippingZones.AddAsync(zone, cancellationToken);

    public void Remove(ShippingZone zone) => _dbContext.ShippingZones.Remove(zone);

    public async Task<ShippingZone?> GetByIdAsync(ShippingZoneId id, CancellationToken cancellationToken = default)
        => await _dbContext.ShippingZones.FirstOrDefaultAsync(z => z.Id == id, cancellationToken);

    public async Task<ShippingZone?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Même normalisation que l'agrégat à la création : sans elle, une zone créée
        // sous « Grand Nokoué » resterait introuvable par « grand-nokoue », le code
        // exact que renvoient pourtant les écrans.
        var normalized = (code ?? string.Empty).Trim().ToLowerInvariant();
        return await _dbContext.ShippingZones.FirstOrDefaultAsync(z => z.Code == normalized, cancellationToken);
    }

    /// <summary>
    /// TOUTES les zones, en une requête et sans filtre.
    ///
    /// Le tri place le repli en dernier : c'est l'ordre d'affichage attendu en
    /// administration, et il ne change rien au résolveur, qui départage sur la
    /// spécificité et non sur l'ordre de lecture.
    ///
    /// Pas de pagination, volontairement. Le Bénin compte 77 communes ; une grille
    /// qui aurait besoin de pagination serait le signe d'un tout autre modèle
    /// tarifaire, et cette méthode serait alors la moindre des choses à revoir.
    /// </summary>
    public async Task<IReadOnlyList<ShippingZone>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.ShippingZones
            .OrderBy(z => z.IsFallback)
            .ThenBy(z => z.Name)
            .ToListAsync(cancellationToken);
}
