using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CategoryId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.ParentId);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();

        builder.Property(c => c.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Path).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.ImageUrl).HasMaxLength(2000);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.AttributeSchema)
            .HasColumnType("jsonb")
            .HasColumnName("attribute_schema")
            .IsRequired();

        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => c.Path);
        builder.HasIndex(c => c.ParentId);

        builder.Ignore(c => c.DomainEvents);
    }
}
