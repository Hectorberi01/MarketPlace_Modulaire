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
        // Colonnes de correction. Valeur par défaut 0 côté BASE, et non seulement côté
        // domaine : les instantanés déjà écrits n'ont pas ces colonnes, et une valeur
        // nulle rendrait `NetGmv` nul pour tout l'historique.
        builder.Property(s => s.RefundedAmount).HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(s => s.RefundsCount).HasDefaultValue(0).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();
        // `NetGmv` est calculée : elle se déduit de deux colonnes présentes, la stocker
        // créerait une troisième vérité à tenir en cohérence.
        builder.Ignore(s => s.NetGmv);

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

internal sealed class ProcessedRefundConfiguration : IEntityTypeConfiguration<ProcessedRefund>
{
    public void Configure(EntityTypeBuilder<ProcessedRefund> builder)
    {
        builder.ToTable("processed_refunds");

        // Clé COMPOSITE, contrairement à `processed_orders` : une même commande peut
        // être remboursée plusieurs fois — plusieurs retours sur des offres
        // différentes, plus un geste commercial. Une clé sur la seule commande aurait
        // fait disparaître le deuxième remboursement en silence.
        builder.HasKey(p => new { p.Source, p.SourceId });
        builder.Property(p => p.Source).HasMaxLength(32).IsRequired();
        builder.Property(p => p.SourceId).ValueGeneratedNever();
        builder.Property(p => p.OrderId).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.ProcessedAtUtc).IsRequired();

        // Le plafond par commande somme ces lignes à chaque remboursement : sans index,
        // c'est un balayage complet de la table sur un chemin appelé à chaud.
        builder.HasIndex(p => p.OrderId);
    }
}
