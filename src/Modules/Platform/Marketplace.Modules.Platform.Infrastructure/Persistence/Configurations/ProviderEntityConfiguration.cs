using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class ProviderEntityConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("providers");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProviderId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired();
        builder.Property(p => p.Priority).IsRequired();

        builder.Property(p => p.SettingsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.SecretsJson).HasColumnType("jsonb").IsRequired();

        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedByUserId).IsRequired();

        // Un seul enregistrement par (canal, code) : la contrainte est en base et pas
        // seulement dans le handler. Deux requêtes concurrentes passeraient toutes
        // deux le test applicatif, et le canal se retrouverait avec deux lignes
        // « Email / Resend » dont l'une resterait invisible à l'écran.
        builder.HasIndex(p => new { p.Channel, p.Code }).IsUnique();

        // Lecture du chemin chaud : les actifs d'un canal, par priorité.
        builder.HasIndex(p => new { p.Channel, p.IsEnabled, p.Priority });

        // Deux administrateurs sur le même écran : le second reçoit un 409 au lieu
        // d'écraser en silence les réglages du premier.
        builder.UsePostgresRowVersion();

        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class ProviderChangeLogConfiguration : IEntityTypeConfiguration<ProviderChangeLog>
{
    public void Configure(EntityTypeBuilder<ProviderChangeLog> builder)
    {
        builder.ToTable("provider_change_log");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // AUCUNE CLÉ ÉTRANGÈRE VERS `providers`, et c'est voulu : le journal doit
        // survivre à la suppression du fournisseur. C'est précisément la suppression
        // qu'on veut pouvoir retrouver. Sans navigation déclarée de part et d'autre,
        // EF n'en déduit aucune relation.
        builder.Property(l => l.ProviderId)
            .HasConversion(id => id.Value, value => new ProviderId(value))
            .IsRequired();

        builder.Property(l => l.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Code).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Detail).HasMaxLength(500).IsRequired();
        builder.Property(l => l.ActorUserId).IsRequired();
        builder.Property(l => l.AtUtc).IsRequired();

        builder.HasIndex(l => l.AtUtc);
        builder.HasIndex(l => l.ProviderId);
    }
}
