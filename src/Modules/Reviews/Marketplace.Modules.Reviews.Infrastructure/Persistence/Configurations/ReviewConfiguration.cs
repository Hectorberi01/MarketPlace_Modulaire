using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ReviewId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.ProductId).IsRequired();
        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.OrderId).IsRequired();

        builder.Property(r => r.Rating)
            .HasConversion(rating => rating.Value, value => Rating.Create(value).Value)
            .HasColumnName("rating")
            .IsRequired();

        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.IsVerifiedPurchase).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        builder.HasIndex(r => r.ProductId);
        builder.HasIndex(r => new { r.BuyerId, r.ProductId, r.OrderId }).IsUnique();

        builder.Ignore(r => r.DomainEvents);
    }
}
