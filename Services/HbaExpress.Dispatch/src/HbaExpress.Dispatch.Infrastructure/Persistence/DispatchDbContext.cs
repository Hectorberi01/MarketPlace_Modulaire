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

    /// <summary>
    /// Une transaction, un enregistrement, une validation. C'est elle qui donne leur
    /// durée de vie aux verrous posés par les lectures réservantes du dépôt.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        var result = await work(cancellationToken);

        // SaveChanges à l'intérieur de la transaction ne valide rien : il écrit. C'est
        // CommitAsync qui valide — et qui relâche les verrous, pas avant.
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DispatchDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
