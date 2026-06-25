using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Recommendations.Domain.Recommendations;

namespace Marketplace.Modules.Recommendations.Infrastructure.Persistence.Configurations;

internal sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> builder)
    {
        builder.ToTable("recommendations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.ContextProductId);
        builder.Property(r => r.UserId);
        builder.Property(r => r.Score).IsRequired();
        builder.Property(r => r.GeneratedAtUtc).IsRequired();

        builder.Property<List<Guid>>("_recommendedProductIds")
            .HasColumnName("recommended_product_ids")
            .HasColumnType("uuid[]")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Ignore(r => r.RecommendedProductIds);

        builder.HasIndex(r => new { r.Type, r.ContextProductId });
        builder.HasIndex(r => new { r.Type, r.UserId });
    }
}
