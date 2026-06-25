using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Infrastructure.Persistence.Configurations;

internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ReturnRequestId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.OfferId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.RefundAmount).HasColumnType("numeric(18,2)");
        builder.Property(r => r.RefundId);
        builder.Property(r => r.Carrier).HasMaxLength(100);
        builder.Property(r => r.TrackingNumber).HasMaxLength(100);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.ResolvedAtUtc);

        builder.HasIndex(r => r.BuyerId);
        builder.HasIndex(r => r.SellerId);
        builder.HasIndex(r => new { r.OrderId, r.OfferId });

        builder.Ignore(r => r.DomainEvents);
    }
}
