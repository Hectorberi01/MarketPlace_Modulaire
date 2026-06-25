using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new ShipmentId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.OrderId).IsRequired();
        builder.Property(s => s.SellerId).IsRequired();
        builder.Property(s => s.BuyerId).IsRequired();
        builder.Property(s => s.ShipFromLocationId).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Carrier).HasMaxLength(100);
        builder.Property(s => s.TrackingNumber).HasMaxLength(100);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.ShippedAtUtc);
        builder.Property(s => s.DeliveredAtUtc);

        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey("ShipmentId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => s.OrderId);
        builder.HasIndex(s => s.SellerId);

        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> builder)
    {
        builder.ToTable("shipment_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasIndex("ShipmentId");
    }
}
