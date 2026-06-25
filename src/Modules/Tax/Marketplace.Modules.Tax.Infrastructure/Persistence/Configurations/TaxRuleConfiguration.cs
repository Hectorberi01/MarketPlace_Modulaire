using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Infrastructure.Persistence.Configurations;

internal sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    public void Configure(EntityTypeBuilder<TaxRule> builder)
    {
        builder.ToTable("tax_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new TaxRuleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.Jurisdiction).HasMaxLength(20).IsRequired();
        builder.Property(r => r.CategoryId);
        builder.Property(r => r.Rate).HasColumnType("numeric(6,4)").IsRequired();
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.EffectiveFromUtc).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        builder.HasIndex(r => new { r.Jurisdiction, r.CategoryId });
        builder.HasIndex(r => r.IsActive);

        builder.Ignore(r => r.DomainEvents);
    }
}
