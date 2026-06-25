using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.RoleId).IsRequired();

        // Relation vers User : la FK ombre « UserId » est créée ici (avant l'index)
        // et prend le type de la clé principale (UserId, mappé en uuid).
        builder.HasOne<User>()
            .WithMany(u => u.RoleAssignments)
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        // Un même rôle n'est assigné qu'une fois par utilisateur.
        builder.HasIndex("UserId", "RoleId").IsUnique();
    }
}
