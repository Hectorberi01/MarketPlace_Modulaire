using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Infrastructure.Persistence;

/// <summary>DbContext du module Reviews (schéma « reviews »).</summary>
public sealed class ReviewsDbContext : ModuleDbContext, IReviewsUnitOfWork
{
    public const string SchemaName = "reviews";

    public ReviewsDbContext(
        DbContextOptions<ReviewsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Review> Reviews => Set<Review>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
