using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Reviews.Application;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Infrastructure.Persistence;

/// <summary>DbContext du module Reviews (schéma « reviews »).</summary>
public sealed class ReviewsDbContext : ModuleDbContext, IReviewsUnitOfWork
{
    public const string SchemaName = "reviews";

    private readonly ICacheService _cache;

    public ReviewsDbContext(
        DbContextOptions<ReviewsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Review> Reviews => Set<Review>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Invalidation du cache des avis.
    ///
    /// Toute écriture sur un avis fait tomber DEUX clés du même produit : la liste
    /// d'avis, et la note agrégée. Ce sont deux vues d'une même vérité — publier un
    /// avis sans recalculer la note afficherait « 12 avis » sous une moyenne qui n'en
    /// compte que 11. Un utilisateur peut compter, et il comptera.
    ///
    /// La modération est concernée au même titre qu'une publication : retirer un avis
    /// change la moyenne autant qu'en ajouter un.
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

        foreach (var entry in ChangeTracker.Entries<Review>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            keys.Add(ReviewsCacheKeys.Rating(entry.Entity.ProductId));
            keys.Add(ReviewsCacheKeys.ByProduct(entry.Entity.ProductId));
        }

        return [.. keys];
    }
}
