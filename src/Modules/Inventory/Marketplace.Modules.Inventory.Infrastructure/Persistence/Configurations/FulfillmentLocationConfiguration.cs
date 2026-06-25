using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Inventory.Domain.Locations;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class FulfillmentLocationConfiguration : IEntityTypeConfiguration<FulfillmentLocation>
{
    public void Configure(EntityTypeBuilder<FulfillmentLocation> builder)
    {
        builder.ToTable("fulfillment_locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new FulfillmentLocationId(value))
            .ValueGeneratedNever();

        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.OwnerId);
        builder.Property(l => l.CreatedOnUtc).IsRequired();

        // Address (VO) en type owned.
        builder.OwnsOne(l => l.Address, address =>
        {
            address.Property(a => a.Line).HasColumnName("address_line").HasMaxLength(500).IsRequired();
            address.Property(a => a.City).HasColumnName("address_city").HasMaxLength(150).IsRequired();
            address.Property(a => a.Country).HasColumnName("address_country").HasMaxLength(100).IsRequired();
            address.Property(a => a.Latitude).HasColumnName("address_latitude");
            address.Property(a => a.Longitude).HasColumnName("address_longitude");
        });
        builder.Navigation(l => l.Address).IsRequired();

        builder.HasIndex(l => l.OwnerId);

        builder.Ignore(l => l.DomainEvents);
    }
}
