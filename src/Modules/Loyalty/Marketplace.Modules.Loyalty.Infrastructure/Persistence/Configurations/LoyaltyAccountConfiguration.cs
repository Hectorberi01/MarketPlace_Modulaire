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

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // CORRECTION D'UNE AFFIRMATION ANTÉRIEURE (voir doc 22). Ce commentaire disait
        // que, sans IsRequired(), EF « sèvrerait » les enfants retirés d'une collection
        // (UPDATE enfant SET FK = NULL) et produirait des orphelins. C'est FAUX ici : le
        // `OnDelete(DeleteBehavior.Cascade)` ci-dessous gouverne aussi le sort des orphelins,
        // et avec Cascade, un enfant retiré est SUPPRIMÉ. Les données de production l'ont
        // confirmé (10 commandes confirmées, zéro réservation orpheline).
        //
        // Ce qui restait vrai, en revanche : EF s'apprêtait à rendre cette colonne NULLABLE
        // pour aligner la base sur un modèle qui la déclarait facultative — alors qu'elle ne
        // l'est pas. On aurait perdu la seule garantie qui ne dépende d'aucun réglage.
        //
        // IsRequired() sert donc à : (1) dire la vérité — un enfant sans parent n'a aucun
        // sens métier ; (2) maintenir le NOT NULL en base, qui refuse un orphelin quelle que
        // soit sa provenance (une ligne orpheline a été trouvée en production, et on ignore
        // ce qui l'a créée) ; (3) rendre le comportement indépendant du OnDelete — dont le
        // retrait, geste anodin en apparence, ferait RÉELLEMENT basculer en sévérance.
        builder.HasMany(a => a.Transactions)
            .WithOne()
            .HasForeignKey("LoyaltyAccountId")
            .IsRequired()
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
