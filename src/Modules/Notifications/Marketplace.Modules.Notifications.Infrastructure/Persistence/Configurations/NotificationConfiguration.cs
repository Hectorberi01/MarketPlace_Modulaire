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
        builder.Property(n => n.CreatedAtUtc).IsRequired();
        builder.Property(n => n.SentAtUtc);
        builder.Property(n => n.ReadAtUtc);

        builder.HasIndex(n => new { n.RecipientUserId, n.Status });

        builder.Ignore(n => n.DomainEvents);
    }
}
