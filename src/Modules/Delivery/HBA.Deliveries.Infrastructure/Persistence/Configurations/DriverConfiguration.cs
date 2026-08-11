using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DriverId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.FullName).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Phone).HasMaxLength(20).IsRequired();

        builder.Property(d => d.Vehicle).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.AccountStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(d => d.Availability).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(d => d.RegisteredAtUtc).IsRequired();
        builder.Property(d => d.VerifiedAtUtc);
        builder.Property(d => d.StatusReason).HasMaxLength(300);
        builder.Property(d => d.LastPositionAtUtc);
        builder.Property(d => d.CompletedDeliveries).IsRequired();

        builder.OwnsOne(d => d.LastKnownPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("last_latitude");
            position.Property(p => p.Longitude).HasColumnName("last_longitude");
        });

        // ─────────────────────────────────────────────────────────────────────
        // UN COMPTE UTILISATEUR = UN SEUL LIVREUR.
        //
        // La contrainte est vérifiée dans RegisterDriverCommandHandler, mais un
        // contrôle applicatif ne survit pas à deux requêtes simultanées : c'est
        // exactement ce que produit un double appui sur « S'inscrire » depuis un
        // réseau lent. La base est le seul endroit où cette règle tient vraiment.
        //
        // Sans elle, la même personne recevrait deux propositions pour la même
        // course, et en refuserait une des deux — le dispatch la compterait comme
        // un refus.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(d => d.UserId)
            .IsUnique()
            .HasDatabaseName("ux_drivers_user");

        // Le dispatch ne pose qu'une question à cette table : « qui peut recevoir
        // une proposition ? ». L'index la sert directement.
        builder.HasIndex(d => new { d.AccountStatus, d.Availability })
            .HasDatabaseName("ix_drivers_dispatchable");
    }
}
