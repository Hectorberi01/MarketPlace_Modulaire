using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Cart.Domain.Carts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<CartAggregate>
{
    public void Configure(EntityTypeBuilder<CartAggregate> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.BuyerId).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey("CartId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.BuyerId, c.Status });

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.OfferId).IsRequired();
        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.CategoryId).IsRequired();
        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ShipFromLocationId).IsRequired();
        builder.Property(i => i.UnitBaseAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasIndex("CartId", "OfferId").IsUnique();
    }
}
