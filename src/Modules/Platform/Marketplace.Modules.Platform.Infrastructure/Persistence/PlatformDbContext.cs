using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Platform.Application.Abstractions;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Infrastructure.Persistence;

/// <summary>DbContext du module Platform (schéma « platform »).</summary>
public sealed class PlatformDbContext : ModuleDbContext, IPlatformUnitOfWork
{
    public const string SchemaName = "platform";

    public PlatformDbContext(
        DbContextOptions<PlatformDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<ProviderChangeLog> ProviderChangeLogs => Set<ProviderChangeLog>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
