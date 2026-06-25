using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ExpiresOnUtc).IsRequired();
        builder.Property(t => t.CreatedOnUtc).IsRequired();
        builder.Property(t => t.RevokedOnUtc);

        // Relation vers User : la FK ombre « UserId » est créée ici (avant l'index)
        // et prend le type de la clé principale (UserId, mappé en uuid).
        builder.HasOne<User>()
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash);
        builder.HasIndex("UserId");
    }
}
