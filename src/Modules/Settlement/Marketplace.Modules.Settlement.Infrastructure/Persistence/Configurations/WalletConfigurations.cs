using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence.Configurations;

internal sealed class SellerWalletConfiguration : IEntityTypeConfiguration<SellerWallet>
{
    public void Configure(EntityTypeBuilder<SellerWallet> builder)
    {
        builder.ToTable("seller_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new SellerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.PendingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.HasIndex(w => w.SellerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class PlatformWalletConfiguration : IEntityTypeConfiguration<PlatformWallet>
{
    public void Configure(EntityTypeBuilder<PlatformWallet> builder)
    {
        builder.ToTable("platform_wallet");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.CommissionBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ProviderFeeBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ShippingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("withdrawals");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.ProviderRef).HasMaxLength(200);
        builder.Property(w => w.FailureReason).HasMaxLength(500);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CompletedAtUtc);

        builder.HasIndex(w => w.SellerId);

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Account).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Reason).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ReferenceType).HasMaxLength(30);
        builder.Property(t => t.ReferenceId);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.HasIndex(t => new { t.OwnerId, t.CreatedAtUtc });

        builder.Ignore(t => t.DomainEvents);
    }
}
