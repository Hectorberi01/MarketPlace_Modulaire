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

        // Réessais. Valeurs par défaut choisies pour que les lignes DÉJÀ EN BASE se
        // comportent exactement comme avant : 0 tentative, aucune temporisation, aucune
        // lettre morte. Aucune reprise de données n'est nécessaire.
        builder.Property(m => m.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.NextAttemptAtUtc);
        builder.Property(m => m.DeadLetteredOnUtc);

        // ─────────────────────────────────────────────────────────────────────────
        // INDEX DE LA FILE ÉLIGIBLE.
        //
        // C'est LA requête du processeur : exécutée toutes les 5 secondes, sur 25 tables,
        // indéfiniment. L'ancien index portait sur `ProcessedOnUtc` seul — il ne suffit
        // plus : la clause filtre désormais aussi sur DeadLetteredOnUtc et NextAttemptAtUtc,
        // et ordonne sur OccurredOnUtc.
        //
        // L'index est PARTIEL, et c'est le point important : il ne contient que les lignes
        // non traitées et non enterrées — une poignée à tout instant. Les millions de lignes
        // TRAITÉES, qui s'accumulent dans la table sans jamais en sortir, n'y entrent
        // jamais. C'est ce qui garde cette requête à coût constant quelle que soit l'histoire
        // de la table.
        //
        // ⚠️ Noms de colonnes en PascalCase entre guillemets doubles : ce projet n'applique
        // AUCUNE convention snake_case sur les colonnes (vérifié dans le ModelSnapshot).
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(m => new { m.NextAttemptAtUtc, m.OccurredOnUtc })
            .HasFilter("\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL")
            .HasDatabaseName("ix_outbox_messages_pending");

        // Les lettres mortes sont rares, mais doivent se lister instantanément dans la
        // console d'admin. Index partiel, donc quasi vide en régime normal.
        builder.HasIndex(m => m.DeadLetteredOnUtc)
            .HasFilter("\"DeadLetteredOnUtc\" IS NOT NULL")
            .HasDatabaseName("ix_outbox_messages_dead_letters");
    }
}
