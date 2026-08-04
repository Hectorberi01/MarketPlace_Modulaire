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
            address.Property(a => a.CommuneCode).HasColumnName("address_commune_code").HasMaxLength(40).IsRequired();
            address.Property(a => a.Quartier).HasColumnName("address_quartier").HasMaxLength(120);
            address.Property(a => a.Landmark).HasColumnName("address_landmark").HasMaxLength(200);
            address.Property(a => a.Line).HasColumnName("address_line").HasMaxLength(500);
            address.Property(a => a.CountryCode).HasColumnName("address_country_code").HasMaxLength(2).IsRequired();
            address.Property(a => a.Latitude).HasColumnName("address_latitude");
            address.Property(a => a.Longitude).HasColumnName("address_longitude");
            address.Ignore(a => a.CommuneName);
        });
        builder.Navigation(l => l.Address).IsRequired();

        builder.HasIndex(l => l.OwnerId);

        builder.Ignore(l => l.DomainEvents);
    }
}
