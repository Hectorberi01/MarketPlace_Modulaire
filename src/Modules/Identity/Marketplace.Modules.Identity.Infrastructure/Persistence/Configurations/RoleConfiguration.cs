using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new RoleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.IsSystem).IsRequired();

        // Permissions stockées en text[] natif PostgreSQL (codes validés par le VO),
        // mappées sur le champ privé _permissions.
        builder.Property<List<string>>("_permissions")
            .HasColumnName("permissions")
            .HasColumnType("text[]")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        builder.Ignore(r => r.Permissions);

        builder.HasIndex(r => r.Name).IsUnique();

        builder.Ignore(r => r.DomainEvents);
    }
}
