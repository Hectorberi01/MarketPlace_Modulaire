using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InventoryItemId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(i => i.LocationId).IsRequired();
        builder.Property(i => i.OnHand).IsRequired();
        builder.Property(i => i.ReorderThreshold).IsRequired();

        // Reserved et Available sont calculés à partir des réservations.
        builder.Ignore(i => i.Reserved);
        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);

        builder.HasMany(i => i.Reservations)
            .WithOne()
            .HasForeignKey("InventoryItemId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Sku);
        builder.HasIndex(i => new { i.Sku, i.LocationId }).IsUnique();

        builder.Ignore(i => i.DomainEvents);
    }
}
