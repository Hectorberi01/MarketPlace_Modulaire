using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Fraud.Domain.Risk;

namespace Marketplace.Modules.Fraud.Infrastructure.Persistence.Configurations;

internal sealed class RiskAssessmentConfiguration : IEntityTypeConfiguration<RiskAssessment>
{
    public void Configure(EntityTypeBuilder<RiskAssessment> builder)
    {
        builder.ToTable("risk_assessments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new RiskAssessmentId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.SubjectType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.SubjectId).IsRequired();
        builder.Property(a => a.Score).IsRequired();
        builder.Property(a => a.Signals).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.EvaluatedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.SubjectType, a.SubjectId, a.EvaluatedAtUtc });

        builder.Ignore(a => a.DomainEvents);
    }
}
