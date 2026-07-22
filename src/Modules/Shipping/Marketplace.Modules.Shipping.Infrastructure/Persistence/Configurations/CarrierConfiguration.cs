using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Shipping.Domain.Carriers;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence.Configurations;

internal sealed class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> builder)
    {
        builder.ToTable("carriers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CarrierId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.TrackingUrlTemplate).HasMaxLength(300);
        builder.Property(c => c.LogoUrl).HasMaxLength(500);
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // Le code identifie le transporteur de façon unique (référencé par les vendeurs).
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Ignore(c => c.DomainEvents);
    }
}
