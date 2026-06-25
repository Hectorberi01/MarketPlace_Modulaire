using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasConversion(id => id.Value, value => new UserId(value))
            .ValueGeneratedNever();

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.PhoneNumber)
            .HasConversion(phone => phone.Value, value => PhoneNumber.Create(value).Value)
            .HasColumnName("phone_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.EmailVerified).IsRequired();
        builder.Property(u => u.MfaEnabled).IsRequired();
        builder.Property(u => u.MfaSecret).HasMaxLength(200);
        builder.Property(u => u.SecurityStamp).IsRequired();
        builder.Property(u => u.EmailVerificationTokenHash).HasMaxLength(200);
        builder.Property(u => u.EmailVerificationExpiresOnUtc);
        builder.Property(u => u.CreatedOnUtc).IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.PhoneNumber).IsUnique();

        // Les relations vers les enfants (et leurs index sur la FK ombre) sont
        // définies dans leurs propres configurations, pour que la Fche FK soit
        // FK ombre soit créée avant d'être indexée. Les navigations en lecture seule utilisent
        // automatiquement leur champ de stockage (_roleAssignments, _refreshTokens).

        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.RoleIds);
    }
}
