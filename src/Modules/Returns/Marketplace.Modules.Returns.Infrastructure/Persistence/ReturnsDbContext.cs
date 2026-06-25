using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Infrastructure.Persistence;

/// <summary>DbContext du module Returns (schéma « returns »).</summary>
public sealed class ReturnsDbContext : ModuleDbContext, IReturnsUnitOfWork
{
    public const string SchemaName = "returns";

    public ReturnsDbContext(
        DbContextOptions<ReturnsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReturnsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
