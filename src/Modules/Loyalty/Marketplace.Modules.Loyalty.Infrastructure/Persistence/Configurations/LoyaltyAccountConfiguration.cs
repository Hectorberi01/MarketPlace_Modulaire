using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Loyalty.Domain.Accounts;

namespace Marketplace.Modules.Loyalty.Infrastructure.Persistence.Configurations;

internal sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        builder.ToTable("loyalty_accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new LoyaltyAccountId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.PointsBalance).IsRequired();
        builder.Property(a => a.LifetimePoints).IsRequired();
        builder.Property(a => a.Tier).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasMany(a => a.Transactions)
            .WithOne()
            .HasForeignKey("LoyaltyAccountId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => a.UserId).IsUnique();

        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class PointTransactionConfiguration : IEntityTypeConfiguration<PointTransaction>
{
    public void Configure(EntityTypeBuilder<PointTransaction> builder)
    {
        builder.ToTable("point_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Amount).IsRequired();
        builder.Property(t => t.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RelatedOrderId);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.HasIndex(t => t.RelatedOrderId);
    }
}
