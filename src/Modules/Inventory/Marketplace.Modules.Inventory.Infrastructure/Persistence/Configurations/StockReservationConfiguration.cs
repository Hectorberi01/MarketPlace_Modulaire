using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();

        // La relation et la FK ombre « InventoryItemId » sont définies par le
        // parent (InventoryItemConfiguration) ; EF crée l'index de FK.
        builder.HasIndex(r => r.OrderId);
    }
}
