using HbaExpress.Dispatch.Domain.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HbaExpress.Dispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// La file sortante vers la marketplace.
///
/// PAS DE JETON DE CONCURRENCE ICI, contrairement aux courses. Un seul drain écrit dans
/// cette table (voir OutboundDrainService), et le pire cas d'un doublon — deux drains
/// concurrents envoyant le même message — est absorbé par l'idempotence de la
/// marketplace. Poser un verrou coûterait une exception à traiter sans rien protéger de
/// plus.
/// </summary>
public sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(80).IsRequired();

        // Le corps JSON. Pas de longueur maximale : un brief de course multi-vendeur
        // n'a pas de taille bornée, et tronquer un message d'argent serait pire que
        // de le stocker en entier.
        builder.Property(m => m.Payload).IsRequired();

        builder.Property(m => m.LastError).HasMaxLength(1000);

        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.NextAttemptAtUtc).IsRequired();
        builder.Property(m => m.AttemptCount).IsRequired();

        // INDEX FILTRÉ SUR LES SEULS MESSAGES EN ATTENTE. La table grossit indéfiniment
        // — on ne supprime rien, ni les envoyés ni les abandonnés — mais la requête du
        // drain ne regarde que la poignée de lignes encore à traiter. Sans le filtre,
        // l'index grandirait avec l'historique et la lecture ralentirait chaque mois.
        builder.HasIndex(m => m.NextAttemptAtUtc)
            .HasFilter("\"SentAtUtc\" IS NULL AND \"DeadLetteredAtUtc\" IS NULL")
            .HasDatabaseName("ix_outbound_messages_pending");

        // Pour la supervision : retrouver les abandons sans balayer la table.
        builder.HasIndex(m => m.DeadLetteredAtUtc)
            .HasFilter("\"DeadLetteredAtUtc\" IS NOT NULL")
            .HasDatabaseName("ix_outbound_messages_dead_lettered");
    }
}
