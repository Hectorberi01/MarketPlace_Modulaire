using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Marketing.Application.Abstractions;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;
using Marketplace.Modules.Marketing.Domain.Legal;

namespace Marketplace.Modules.Marketing.Infrastructure.Persistence;

/// <summary>DbContext du module Marketing (schéma « marketing »).</summary>
public sealed class MarketingDbContext : ModuleDbContext, IMarketingUnitOfWork
{
    public const string SchemaName = "marketing";

    public MarketingDbContext(
        DbContextOptions<MarketingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
