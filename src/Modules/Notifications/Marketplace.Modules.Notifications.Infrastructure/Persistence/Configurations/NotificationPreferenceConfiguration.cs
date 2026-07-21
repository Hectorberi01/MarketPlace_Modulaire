using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Notifications.Domain.Preferences;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence.Configurations;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();

        // Catégories coupées -> text[] natif PostgreSQL.
        builder.Property(p => p.MutedCategories)
            .HasColumnType("text[]")
            .HasColumnName("muted_categories")
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc).IsRequired();
    }
}
