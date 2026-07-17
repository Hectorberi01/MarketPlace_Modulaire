using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Offers.Application;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Infrastructure.Persistence;

/// <summary>DbContext du module Offers (schéma « offers »).</summary>
public sealed class OffersDbContext : ModuleDbContext, IOffersUnitOfWork
{
    public const string SchemaName = "offers";

    private readonly ICacheService _cache;

    public OffersDbContext(
        DbContextOptions<OffersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Offer> Offers => Set<Offer>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OffersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Invalidation du cache des offres.
    ///
    /// C'est L'INVALIDATION LA PLUS IMPORTANTE DU SYSTÈME, parce que c'est la seule
    /// qui porte sur un prix. Un vendeur qui baisse son prix doit le voir appliqué
    /// tout de suite — pas dans soixante secondes, et surtout pas « quand le TTL
    /// voudra bien ». Le TTL court n'est qu'un filet ; l'invalidation, elle, est la
    /// garantie.
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

        foreach (var entry in ChangeTracker.Entries<Offer>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            keys.Add(OffersCacheKeys.Offer(entry.Entity.Id.Value));
            keys.Add(OffersCacheKeys.OffersByProduct(entry.Entity.ProductId));

            // Si l'offre a changé de produit (rare, mais possible), l'ANCIENNE fiche
            // produit garderait sinon une offre qui n'est plus la sienne.
            var original = entry.Property(o => o.ProductId).OriginalValue;
            if (original != Guid.Empty && original != entry.Entity.ProductId)
            {
                keys.Add(OffersCacheKeys.OffersByProduct(original));
            }
        }

        return [.. keys];
    }
}
