using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Wishlist.Domain.Wishlists;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Marketplace.Modules.Wishlist.Infrastructure.Persistence.Configurations;

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<WishlistAggregate>
{
    public void Configure(EntityTypeBuilder<WishlistAggregate> builder)
    {
        builder.ToTable("wishlists");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WishlistId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.UserId).IsRequired();

        builder.HasMany(w => w.Items)
            .WithOne()
            .HasForeignKey("WishlistId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Une seule liste d'envies par utilisateur.
        builder.HasIndex(w => w.UserId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("wishlist_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.OfferId);
        builder.Property(i => i.PriceAlert).IsRequired();
        builder.Property(i => i.StockAlert).IsRequired();
        builder.Property(i => i.AddedAtUtc).IsRequired();

        builder.HasIndex(i => i.ProductId);
    }
}
