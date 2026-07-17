using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Application;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Sellers (schéma « sellers »). Hérite de ModuleDbContext
/// pour l'Unit of Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class SellersDbContext : ModuleDbContext, ISellerUnitOfWork
{
    public const string SchemaName = "sellers";

    private readonly ICacheService _cache;

    public SellersDbContext(
        DbContextOptions<SellersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Seller> Sellers => Set<Seller>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SellersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Invalidation du cache — point de passage unique, comme dans Catalog.
    ///
    /// Le vendeur est mis en cache sous DEUX clés : par identifiant de vendeur, et
    /// par identifiant d'utilisateur (la connexion vendeur passe par là). Les deux
    /// doivent tomber ensemble. N'en évincer qu'une, c'est laisser l'autre servir
    /// un nom de boutique périmé — un bug d'autant plus retors qu'il ne se
    /// manifeste que sur l'un des deux chemins de lecture.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var keysToEvict = CollectCacheKeysToEvict();

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    private List<string> CollectCacheKeysToEvict()
    {
        var keys = new HashSet<string>();

        // Les documents KYB sont les enfants de l'agrégat (FK shadow « SellerId »).
        // Leur vérification change le statut affiché du vendeur : le résumé en cache
        // doit tomber, même si l'entité Seller n'a pas été touchée directement.
        var touchedSellers = new HashSet<Guid>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Seller seller:
                    touchedSellers.Add(seller.Id.Value);

                    // La clé « par utilisateur » n'est déductible que d'ici : les
                    // documents ne portent pas l'UserId.
                    keys.Add(SellersCacheKeys.SellerByUser(seller.UserId));
                    break;

                case KybDocument:
                    var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "SellerId");
                    if ((property?.CurrentValue ?? property?.OriginalValue) is Guid sellerId && sellerId != Guid.Empty)
                    {
                        touchedSellers.Add(sellerId);
                    }
                    break;
            }
        }

        foreach (var sellerId in touchedSellers)
        {
            keys.Add(SellersCacheKeys.Seller(sellerId));

            // Retrouve l'UserId sur l'agrégat suivi, quand seul un document a bougé.
            var parent = ChangeTracker.Entries<Seller>()
                .Select(e => e.Entity)
                .FirstOrDefault(s => s.Id.Value == sellerId);

            if (parent is not null)
            {
                keys.Add(SellersCacheKeys.SellerByUser(parent.UserId));
            }
        }

        return [.. keys];
    }
}
