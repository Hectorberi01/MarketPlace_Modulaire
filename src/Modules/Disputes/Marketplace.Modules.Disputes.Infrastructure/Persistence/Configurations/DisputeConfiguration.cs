using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Infrastructure.Persistence.Configurations;

internal sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("disputes");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DisputeId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.OrderId).IsRequired();
        builder.Property(d => d.RaisedBy).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Resolution).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.RefundAmount).HasPrecision(18, 2);
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.ResolvedAtUtc);

        builder.HasMany(d => d.Messages)
            .WithOne()
            .HasForeignKey("DisputeId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.OrderId);
        builder.HasIndex(d => d.RaisedBy);

        builder.Ignore(d => d.DomainEvents);
    }
}

internal sealed class DisputeMessageConfiguration : IEntityTypeConfiguration<DisputeMessage>
{
    public void Configure(EntityTypeBuilder<DisputeMessage> builder)
    {
        builder.ToTable("dispute_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.AuthorId).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.PhotoUrl).HasMaxLength(500);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.HasIndex("DisputeId");
    }
}
