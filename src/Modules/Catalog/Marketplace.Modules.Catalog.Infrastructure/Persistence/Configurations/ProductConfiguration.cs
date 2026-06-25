using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure.Persistence.Converters;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProductId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.SellerId).IsRequired();
        builder.Property(p => p.CategoryId).IsRequired();
        builder.Property(p => p.BrandId);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.Gtin).HasMaxLength(14);
        builder.Property(p => p.Ean).HasMaxLength(14);
        builder.Property(p => p.ProductGroupId);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.CreatedOnUtc).IsRequired();

        builder.Property(p => p.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        // Attributs dynamiques -> jsonb.
        builder.Property(p => p.Attributes)
            .HasConversion(new AttributesJsonConverter(), new AttributesJsonComparer())
            .HasColumnType("jsonb")
            .HasColumnName("attributes")
            .IsRequired();

        // Tags -> text[] natif PostgreSQL (index GIN possible).
        builder.Property(p => p.Tags)
            .HasColumnType("text[]")
            .HasColumnName("tags")
            .IsRequired();

        // Variantes et médias : enfants de l'agrégat, accès par champ.
        builder.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Media)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Media).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.SellerId);
        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.BrandId);
        builder.HasIndex(p => p.ProductGroupId);

        builder.Ignore(p => p.DomainEvents);
    }
}
