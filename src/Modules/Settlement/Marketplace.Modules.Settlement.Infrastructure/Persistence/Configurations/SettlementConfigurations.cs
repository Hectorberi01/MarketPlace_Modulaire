using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence.Configurations;

internal sealed class SellerEarningConfiguration : IEntityTypeConfiguration<SellerEarning>
{
    public void Configure(EntityTypeBuilder<SellerEarning> builder)
    {
        builder.ToTable("seller_earnings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new SellerEarningId(value))
            .ValueGeneratedNever();

        builder.Property(e => e.OrderId).IsRequired();
        builder.Property(e => e.OfferId).IsRequired();
        builder.Property(e => e.SellerId).IsRequired();
        builder.Property(e => e.ProductId).IsRequired();
        builder.Property(e => e.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.ReleasedAtUtc);
        builder.Property(e => e.SettlementBatchId);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.SellerId);
        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc });

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class SettlementBatchConfiguration : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> builder)
    {
        builder.ToTable("settlement_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new SettlementBatchId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.PeriodStartUtc).IsRequired();
        builder.Property(b => b.PeriodEndUtc).IsRequired();
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
        builder.Property(b => b.TotalNet).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
        // ne sévrait PAS pour autant. Le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne aussi le sort des ORPHELINS — avec Cascade, un enfant retiré de la
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production l'ont confirmé.
        //
        // Alors pourquoi IsRequired() ? Pour trois raisons plus modestes et plus sûres :
        //
        //   1. Cette clé étrangère est RÉELLEMENT obligatoire — un enfant sans parent n'a
        //      aucun sens métier. Le modèle le déclarait facultatif. Un modèle qui ment
        //      finit toujours par produire du code qui se trompe.
        //
        //   2. La colonne était NULL-able en base, donc RIEN ne l'interdisait. Une ligne
        //      orpheline a d'ailleurs été trouvée en production (message_reactions) : on
        //      ignore ce qui l'a créée, et c'est précisément le problème. NOT NULL l'aurait
        //      refusée, quelle que soit sa provenance.
        //
        //   3. Sans ça, le comportement dépend d'un réglage FRAGILE : retirer le
        //      `OnDelete(Cascade)` — geste anodin en apparence — ferait réellement basculer
        //      cette relation en sévérance. Avec IsRequired() ET NOT NULL, c'est impossible.
        builder.HasMany(b => b.Payouts)
            .WithOne()
            .HasForeignKey("SettlementBatchId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Payouts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.DomainEvents);
    }
}

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.SellerId).IsRequired();
        builder.Property(p => p.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderRef).HasMaxLength(200);
        builder.Property(p => p.PaidAtUtc);

        // L'index sur la FK « SettlementBatchId » est créé automatiquement par la
        // relation (HasMany dans SettlementBatchConfiguration) ; on ne le déclare
        // pas ici pour ne pas dépendre de l'ordre d'application des configurations.
        builder.HasIndex(p => p.SellerId);
    }
}
