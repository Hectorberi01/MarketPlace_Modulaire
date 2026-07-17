using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Pricing.Infrastructure.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — LE POINT DE SÉRIALISATION DES COUPONS.
        //
        // `Promotion.Redeem()` incrémente TOUJOURS `UsedCount` : toute rédemption salit
        // donc CETTE ligne. Avec le jeton, deux commandes qui consomment le même coupon
        // au même instant ne peuvent pas commiter toutes les deux — la seconde voit son
        // UPDATE toucher 0 ligne et lève DbUpdateConcurrencyException.
        //
        // C'est ce qui rend fiables DEUX contrôles qui, seuls, seraient des vœux pieux :
        //   • le plafond global (`UsageLimit`) — sinon 5 commandes simultanées passent
        //     toutes le « UsedCount < UsageLimit » sur le dernier exemplaire ;
        //   • le plafond par acheteur (`PerUserLimit`), qui repose sur un COUNT lu juste
        //     avant l'écriture — le COUNT et l'UPDATE partagent la transaction, et cette
        //     ligne en est le rendez-vous obligé.
        //
        // Le handler tourne dans l'OutboxProcessor, pas dans une requête HTTP : le
        // conflit n'y devient donc pas un 409, il fait simplement échouer le message,
        // qui sera rejoué au prochain tour (toutes les 5 s). C'est le comportement voulu.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();

        builder.ToTable("promotions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PromotionId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.OwnerId).IsRequired();
        builder.Property(p => p.FundedBy).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Value).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(p => p.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(64);
        builder.Property(p => p.UsageLimit).IsRequired();

        // Plafond par acheteur. Défaut 0 (= illimité) : les promotions déjà en base
        // gardent exactement le comportement qu'elles avaient. Aucune reprise de données.
        builder.Property(p => p.PerUserLimit).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.UsedCount).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Cibles (Product/Category/Seller ids) en uuid[] natif, mappées sur le champ.
        builder.Property<List<Guid>>("_targets")
            .HasColumnName("targets")
            .HasColumnType("uuid[]")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Ignore(p => p.Targets);

        // Période (VO) en owned -> colonnes start_utc / end_utc.
        builder.OwnsOne(p => p.Period, period =>
        {
            period.Property(d => d.StartUtc).HasColumnName("start_utc").IsRequired();
            period.Property(d => d.EndUtc).HasColumnName("end_utc").IsRequired();
        });
        builder.Navigation(p => p.Period).IsRequired();

        // Conditions (record) en owned -> 3 colonnes.
        builder.OwnsOne(p => p.Conditions, conditions =>
        {
            conditions.Property(c => c.MinAmount).HasColumnName("condition_min_amount").HasColumnType("numeric(18,2)");
            conditions.Property(c => c.MinQuantity).HasColumnName("condition_min_quantity");
            conditions.Property(c => c.FirstOrderOnly).HasColumnName("condition_first_order_only").IsRequired();
        });
        builder.Navigation(p => p.Conditions).IsRequired();

        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.Code);

        builder.Ignore(p => p.DomainEvents);
    }
}
