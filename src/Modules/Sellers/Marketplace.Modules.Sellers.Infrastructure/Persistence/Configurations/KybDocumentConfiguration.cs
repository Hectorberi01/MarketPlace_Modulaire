using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence.Configurations;

internal sealed class KybDocumentConfiguration : IEntityTypeConfiguration<KybDocument>
{
    public void Configure(EntityTypeBuilder<KybDocument> builder)
    {
        builder.ToTable("kyb_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(d => d.FileUrl).HasMaxLength(2000).IsRequired();
        builder.Property(d => d.UploadedOnUtc).IsRequired();
        builder.Property(d => d.VerifiedAtUtc);

        // Relation vers Seller : la FK ombre « SellerId » est créée ici (avant
        // l'index) et prend le type de la clé principale (SellerId -> uuid).
        builder.HasOne<Seller>()
            .WithMany(s => s.KybDocuments)
            .HasForeignKey("SellerId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("SellerId");
    }
}
