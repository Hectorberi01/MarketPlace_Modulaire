using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Mapping EF de la table outbox. Chaque module l'applique dans son propre
/// schéma via <c>ApplyConfiguration</c>, ce qui garde l'outbox local au module.
/// </summary>
public sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(500).IsRequired();
        builder.Property(m => m.Content).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredOnUtc).IsRequired();
        builder.Property(m => m.ProcessedOnUtc);
        builder.Property(m => m.Error);

        // Index sur les messages non traités : le processeur ne lit que ceux-là.
        builder.HasIndex(m => m.ProcessedOnUtc);
    }
}
