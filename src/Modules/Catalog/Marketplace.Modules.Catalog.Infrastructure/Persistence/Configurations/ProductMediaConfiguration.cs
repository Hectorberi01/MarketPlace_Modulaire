using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("product_media");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Url).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.AltText).HasMaxLength(500);
        builder.Property(m => m.Position).IsRequired();
        builder.Property(m => m.IsPrimary).IsRequired();
        builder.Property(m => m.ExternalId).HasMaxLength(200);
    }
}
