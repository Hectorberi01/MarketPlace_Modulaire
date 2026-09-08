using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HbaExpress.Dispatch.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF (migrations).</summary>
public sealed class DispatchDbContextFactory : IDesignTimeDbContextFactory<DispatchDbContext>
{
    public DispatchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DISPATCH_DB")
            ?? "Host=localhost;Port=5432;Database=dispatch;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DispatchDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DispatchDbContext.SchemaName))
            .Options;

        return new DispatchDbContext(options);
    }
}
