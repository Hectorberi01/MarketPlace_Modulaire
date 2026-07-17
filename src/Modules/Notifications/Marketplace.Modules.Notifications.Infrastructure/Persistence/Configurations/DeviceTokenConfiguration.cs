using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Notifications.Domain.Devices;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence.Configurations;

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.Token).HasMaxLength(512).IsRequired();
        builder.Property(d => d.Platform).HasMaxLength(20).IsRequired();
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.LastSeenAtUtc).IsRequired();

        // Un jeton (installation d'app) est unique ; recherche rapide par utilisateur.
        builder.HasIndex(d => d.Token).IsUnique();
        builder.HasIndex(d => d.UserId);
    }
}
