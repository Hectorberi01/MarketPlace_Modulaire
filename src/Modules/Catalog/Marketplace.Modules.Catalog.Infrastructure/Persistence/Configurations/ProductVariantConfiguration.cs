using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure.Persistence.Converters;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(v => v.VariantAttributes)
            .HasConversion(new AttributesJsonConverter(), new AttributesJsonComparer())
            .HasColumnType("jsonb")
            .HasColumnName("variant_attributes")
            .IsRequired();

        builder.Property(v => v.Barcode).HasMaxLength(64);
        builder.Property(v => v.WeightGrams).IsRequired();

        // Cast vers le type ValueConverter non générique : la propriété est
        // Dimensions? (nullable), l'overload générique infèrerait un converter
        // nullable et rejetterait le nôtre. EF gère la valeur null tout seul.
        builder.Property(v => v.Dimensions)
            .HasConversion((ValueConverter)new DimensionsJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("dimensions");

        // SKU unique globalement (contrat avec Inventory / Pricing).
        builder.HasIndex(v => v.Sku).IsUnique();
    }
}
