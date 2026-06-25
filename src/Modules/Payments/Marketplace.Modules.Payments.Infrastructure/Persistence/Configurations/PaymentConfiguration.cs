using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OrderId).IsRequired();
        builder.Property(p => p.BuyerId).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Flow).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.CapturedAtUtc);
        builder.Property(p => p.EscrowReleasedAt);
        builder.Ignore(p => p.IsEscrowHeld);

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(p => p.Amount).IsRequired();

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ProviderReference);

        builder.Ignore(p => p.DomainEvents);
    }
}
