using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new BrandId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();

        builder.Property(b => b.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(b => b.LogoUrl).HasMaxLength(2000);
        builder.Property(b => b.Description).HasMaxLength(2000);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(b => b.Slug).IsUnique();

        builder.Ignore(b => b.DomainEvents);
    }
}
