using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Outbound;
using Microsoft.EntityFrameworkCore;

namespace HbaExpress.Dispatch.Infrastructure.Persistence;

/// <summary>
/// Base du service de dispatch. PROPRE, jamais celle de la marketplace : ce service
/// réécrit des positions à la seconde, la marketplace tient de l'argent.
/// </summary>
public sealed class DispatchDbContext : DbContext, IDispatchUnitOfWork
{
    public const string SchemaName = "dispatch";

    public DispatchDbContext(DbContextOptions<DispatchDbContext> options) : base(options)
    {
    }

    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DispatchDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
