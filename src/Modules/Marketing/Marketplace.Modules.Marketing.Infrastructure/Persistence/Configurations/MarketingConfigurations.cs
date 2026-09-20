using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;

namespace Marketplace.Modules.Marketing.Infrastructure.Persistence.Configurations;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CampaignId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.StartUtc).IsRequired();
        builder.Property(c => c.EndUtc).IsRequired();
        builder.Property(c => c.TargetSegment).HasColumnType("jsonb");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // ── PRÉSENTATION PUBLIQUE ───────────────────────────────────────────────────
        // Le slug est UNIQUE, mais seulement parmi les campagnes qui en ont un : un
        // index unique ordinaire aurait interdit d'avoir deux campagnes sans adresse,
        // ce qui est le cas de la plupart. D'où le filtre — PostgreSQL n'applique
        // l'unicité qu'aux lignes retenues.
        builder.Property(c => c.Slug).HasMaxLength(120);
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.HeroImageUrl).HasMaxLength(500);
        builder.Property(c => c.IsPublic).IsRequired();

        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasFilter("\"Slug\" IS NOT NULL");

        builder.Property<List<Guid>>("_promotionIds")
            .HasColumnName("promotion_ids")
            .HasColumnType("uuid[]")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Ignore(c => c.PromotionIds);

        builder.HasIndex(c => c.Status);

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder.ToTable("content_blocks");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new ContentBlockId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.Slot).HasMaxLength(100).IsRequired();
        builder.Property(b => b.Position).IsRequired();
        builder.Property(b => b.Content).HasColumnType("jsonb").IsRequired();
        builder.Property(b => b.StartUtc);
        builder.Property(b => b.EndUtc);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(b => new { b.Slot, b.Status });

        builder.Ignore(b => b.DomainEvents);
    }
}
