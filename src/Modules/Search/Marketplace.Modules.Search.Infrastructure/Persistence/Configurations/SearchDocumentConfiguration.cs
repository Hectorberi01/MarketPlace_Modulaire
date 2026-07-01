using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Search.Domain.Documents;

namespace Marketplace.Modules.Search.Infrastructure.Persistence.Configurations;

internal sealed class SearchDocumentConfiguration : IEntityTypeConfiguration<SearchDocument>
{
    public void Configure(EntityTypeBuilder<SearchDocument> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.ProductId);
        builder.Property(d => d.ProductId).ValueGeneratedNever();

        builder.Property(d => d.SellerId).IsRequired();
        builder.Property(d => d.CategoryId).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(300).IsRequired();
        builder.Property(d => d.Slug).HasMaxLength(300).IsRequired();
        builder.Property(d => d.MinPrice).HasColumnType("numeric(18,2)");
        builder.Property(d => d.Currency).HasMaxLength(3);
        builder.Property(d => d.AverageRating).IsRequired();
        builder.Property(d => d.ReviewCount).IsRequired();
        builder.Property(d => d.UpdatedAtUtc).IsRequired();
        builder.Property(d => d.IsVisible).IsRequired();
        builder.Property(d => d.ImageUrl).HasMaxLength(1000);

        builder.HasIndex(d => d.CategoryId);
        builder.HasIndex(d => d.Name);
        builder.HasIndex(d => d.IsVisible);
    }
}
