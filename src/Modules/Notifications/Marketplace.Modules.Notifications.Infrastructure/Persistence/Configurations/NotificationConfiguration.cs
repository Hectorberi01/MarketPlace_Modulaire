using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, value => new NotificationId(value))
            .ValueGeneratedNever();

        builder.Property(n => n.RecipientUserId).IsRequired();
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Subject).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.RelatedEntityType).HasMaxLength(50).IsRequired();
        builder.Property(n => n.RelatedEntityId);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.DeduplicationKey).HasMaxLength(200);
        builder.Property(n => n.CreatedAtUtc).IsRequired();
        builder.Property(n => n.SentAtUtc);
        builder.Property(n => n.ReadAtUtc);

        builder.HasIndex(n => new { n.RecipientUserId, n.Status });

        // ════════════════════════════════════════════════════════════════════════════
        // LA SEULE GARANTIE QUI TIENNE CONTRE UN REJEU : L'UNICITÉ EN BASE.
        //
        // Il n'y avait ici qu'un index NON unique sur (destinataire, statut), et
        // `NotificationDispatcher.NotifyAsync` créait INCONDITIONNELLEMENT. Un `SELECT`
        // préalable côté application aurait suffi dans le cas nominal, mais pas contre
        // deux tentatives concurrentes du même message : les deux lisent « rien », les
        // deux insèrent. C'est exactement ce que produit un rejeu d'outbox pendant que
        // la tentative précédente est encore en vol.
        //
        // L'index est PARTIEL : il ne s'applique qu'aux notifications qui portent une
        // clé. Les autres — « nouveau message », « colis en transit » — se répètent
        // légitimement et doivent rester libres de le faire ; un index unique global
        // les casserait toutes.
        //
        // Le `SELECT` du dispatcher reste utile comme chemin rapide (il évite le push et
        // l'e-mail avant même d'écrire). Mais c'est CET index qui garantit le résultat :
        // si la course est perdue, l'insertion échoue, le message est rejoué, et la
        // tentative suivante voit la ligne du gagnant et s'arrête.
        // ════════════════════════════════════════════════════════════════════════════
        builder.HasIndex(n => n.DeduplicationKey)
            .IsUnique()
            .HasFilter("\"DeduplicationKey\" IS NOT NULL");

        builder.Ignore(n => n.DomainEvents);
    }
}
