using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Infrastructure.Persistence;

/// <summary>DbContext du module Payments (schéma « payments »).</summary>
public sealed class PaymentsDbContext : ModuleDbContext, IPaymentsUnitOfWork
{
    public const string SchemaName = "payments";

    public PaymentsDbContext(
        DbContextOptions<PaymentsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
