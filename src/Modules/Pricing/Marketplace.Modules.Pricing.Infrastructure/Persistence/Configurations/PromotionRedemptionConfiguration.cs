using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Infrastructure.Persistence.Configurations;

internal sealed class PromotionRedemptionConfiguration : IEntityTypeConfiguration<PromotionRedemption>
{
    public void Configure(EntityTypeBuilder<PromotionRedemption> builder)
    {
        builder.ToTable("promotion_redemptions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PromotionId)
            .HasConversion(id => id.Value, value => new PromotionId(value))
            .IsRequired();

        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.RedeemedAtUtc).IsRequired();

        // ─────────────────────────────────────────────────────────────────────────
        // INDEX D'IDEMPOTENCE — LA SEULE GARANTIE QUI TIENNE SOUS REJEU.
        //
        // L'outbox est AT-LEAST-ONCE : le même OrderConfirmedIntegrationEvent peut
        // être livré deux fois (crash après publication, avant marquage ; ou simple
        // reprise du processeur). Le handler vérifie « déjà consommé ? » avant
        // d'écrire — mais entre ce SELECT et l'INSERT, deux livraisons concurrentes
        // peuvent toutes deux se croire les premières. Une vérification applicative ne
        // ferme JAMAIS cette fenêtre. Seule la base le peut.
        //
        // Une commande consomme donc AU PLUS UNE FOIS une promotion donnée. Un rejeu
        // lève une violation de contrainte, la transaction est annulée, et le coupon
        // n'est pas décompté deux fois pour une seule vente.
        //
        // Nom de colonne en PascalCase entre guillemets doubles : ce projet
        // n'applique AUCUNE convention snake_case sur les propriétés (les noms de
        // TABLES le sont, pas les colonnes). Vérifié dans le ModelSnapshot.
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(r => new { r.PromotionId, r.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_promotion_redemptions_order");

        // Sert le COUNT par acheteur (limite PerUserLimit) sans scan.
        builder.HasIndex(r => new { r.PromotionId, r.BuyerId })
            .HasDatabaseName("ix_promotion_redemptions_buyer");
    }
}
