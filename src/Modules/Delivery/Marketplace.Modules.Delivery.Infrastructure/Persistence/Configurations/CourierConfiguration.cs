using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Infrastructure.Persistence.Configurations;

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CourierId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20).IsRequired();

        // Statut persisté en CHAÎNE : ajouter un statut ne renumérote rien et ne
        // demande aucune reprise de données. Même convention que les portefeuilles.
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.SuspensionReason).HasMaxLength(400);
        builder.Property(c => c.ExternalRef).HasMaxLength(120);
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        // Le compte de versement est un objet-valeur : il n'a pas d'existence propre,
        // il est aplati en colonnes sur la ligne du coursier.
        builder.OwnsOne(c => c.Payout, payout =>
        {
            payout.Property(p => p.Provider).HasColumnName("PayoutProvider").HasMaxLength(40).IsRequired();
            payout.Property(p => p.AccountNumber).HasColumnName("PayoutAccountNumber").HasMaxLength(30).IsRequired();
            payout.Property(p => p.AccountName).HasColumnName("PayoutAccountName").HasMaxLength(120).IsRequired();
        });

        builder.Navigation(c => c.Payout).IsRequired();

        // UN SEUL COMPTE COURSIER PAR UTILISATEUR, imposé par la base et pas seulement
        // par le handler : deux portefeuilles livreur pour la même personne rendraient
        // ses gains invisibles depuis l'un des deux.
        builder.HasIndex(c => c.UserId).IsUnique();

        // La référence externe est la clé par laquelle le service de dispatch nous
        // désigne un coursier. Unique, mais facultative : un coursier peut exister ici
        // avant d'exister là-bas.
        builder.HasIndex(c => c.ExternalRef).IsUnique().HasFilter("\"ExternalRef\" IS NOT NULL");

        builder.HasIndex(c => c.Status);

        builder.Ignore(c => c.DomainEvents);
    }
}
