using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence.Configurations;

internal sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("sellers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SellerId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.ShopName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LogoUrl).HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.KybStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.CommissionRate).HasColumnType("numeric(5,4)").IsRequired();
        builder.Property(s => s.Rating).HasColumnType("numeric(3,2)").IsRequired();
        builder.Property(s => s.SalesCount).IsRequired();
        builder.Property(s => s.CreatedOnUtc).IsRequired();

        // PayoutAccount (VO nullable) sérialisé en jsonb. Cast vers le converter
        // non générique car la propriété est nullable.
        builder.Property(s => s.PayoutAccount)
            .HasConversion((ValueConverter)new PayoutAccountJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("payout_account");

        // Metadata société (VO nullable) en jsonb, null par défaut. Comme PayoutAccount,
        // le converter n'est appliqué qu'aux valeurs non nulles ; NULL reste NULL.
        builder.Property(s => s.Metadata)
            .HasConversion((ValueConverter)new SellerCompanyInfoJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("metadata");

        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasIndex(s => s.ShopName).IsUnique();

        builder.Ignore(s => s.DomainEvents);
    }
}
