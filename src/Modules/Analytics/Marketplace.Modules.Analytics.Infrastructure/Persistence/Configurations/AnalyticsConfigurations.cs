using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Analytics.Domain.Metrics;

namespace Marketplace.Modules.Analytics.Infrastructure.Persistence.Configurations;

internal sealed class MetricSnapshotConfiguration : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> builder)
    {
        builder.ToTable("metric_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.OwnerId);
        builder.Property(s => s.PeriodKey).HasMaxLength(10).IsRequired();
        builder.Property(s => s.Granularity).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.OrdersCount).IsRequired();
        builder.Property(s => s.ItemsSold).IsRequired();
        builder.Property(s => s.GmvAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        // Unicité fonctionnelle d'un instantané : (périmètre, propriétaire, période, granularité).
        builder.HasIndex(s => new { s.Scope, s.OwnerId, s.PeriodKey, s.Granularity }).IsUnique();
    }
}

internal sealed class ProcessedOrderConfiguration : IEntityTypeConfiguration<ProcessedOrder>
{
    public void Configure(EntityTypeBuilder<ProcessedOrder> builder)
    {
        builder.ToTable("processed_orders");

        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();
        builder.Property(p => p.ProcessedAtUtc).IsRequired();
    }
}
