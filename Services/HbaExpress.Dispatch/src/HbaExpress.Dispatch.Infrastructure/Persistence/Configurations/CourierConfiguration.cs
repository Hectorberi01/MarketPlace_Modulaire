using HbaExpress.Dispatch.Domain.Couriers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HbaExpress.Dispatch.Infrastructure.Persistence.Configurations;

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CourierId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.DisplayName).HasMaxLength(120).IsRequired();

        // Persistés en CHAÎNE : ajouter un statut ou un type de véhicule ne renumérote
        // rien et ne demande aucune reprise de données.
        builder.Property(c => c.Standing).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Availability).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Vehicle).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.LastPositionAtUtc);
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // La position est un objet-valeur : aplatie en deux colonnes sur la ligne du
        // coursier. Facultative — un coursier qui n'a jamais ouvert l'application n'en a
        // pas, et c'est un état normal, pas une donnée manquante.
        builder.OwnsOne(c => c.LastPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("LastLatitude");
            position.Property(p => p.Longitude).HasColumnName("LastLongitude");
        });

        // Zone de service : un tableau de texte Postgres, écrit et lu par le CHAMP —
        // la propriété publique n'est qu'une vue en lecture seule.
        builder.PrimitiveCollection<List<string>>("_communeCodes")
            .HasColumnName("CommuneCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.CommuneCodes);
        builder.Ignore(c => c.DomainEvents);

        // ═════════════════════════════════════════════════════════════════════════
        // INDEX DU DISPATCH. C'est LA requête chaude du service : à chaque course
        // créée, on cherche les coursiers actifs et en ligne. Sans cet index, chaque
        // dispatch balaie toute la flotte — y compris ceux qui ont arrêté il y a six
        // mois.
        // ═════════════════════════════════════════════════════════════════════════
        builder.HasIndex(c => new { c.Standing, c.Availability });
    }
}
