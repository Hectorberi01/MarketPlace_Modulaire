using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Pricing;
using HBA.Deliveries.Domain.Webhooks;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>DbContext du module Deliveries (schéma « deliveries »).</summary>
public sealed class DeliveriesDbContext : ModuleDbContext, IDeliveryUnitOfWork
{
    public const string SchemaName = "deliveries";

    public DeliveriesDbContext(
        DbContextOptions<DeliveriesDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Domain.Deliveries.Delivery> Deliveries => Set<Domain.Deliveries.Delivery>();

    public DbSet<Driver> Drivers => Set<Driver>();

    // ── Partenaires (API publique) ──────────────────────────────────────────
    public DbSet<Partner> Partners => Set<Partner>();

    // ── Tarification ────────────────────────────────────────────────────────
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<DeliveryQuote> DeliveryQuotes => Set<DeliveryQuote>();

    // ── Webhooks sortants ───────────────────────────────────────────────────
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveriesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
