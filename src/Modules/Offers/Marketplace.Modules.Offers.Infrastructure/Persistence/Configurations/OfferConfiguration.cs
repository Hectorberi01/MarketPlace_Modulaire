using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Infrastructure.Persistence.Configurations;

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OfferId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.ProductId).IsRequired();
        builder.Property(o => o.SellerId).IsRequired();

        builder.Property(o => o.VariantSku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        // Money (VO) en type owned -> colonnes base_price_amount / _currency.
        builder.OwnsOne(o => o.BasePrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("base_price_amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("base_price_currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(o => o.BasePrice).IsRequired();

        // Détail du modèle de prix « prix vendeur net + majoration plateforme ».
        builder.Property(o => o.SellerPrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();

        // Remise (promo vendeur), toutes nullable. CompareAtPrice = prix acheteur d'origine
        // (barré) en owned optionnel ; OriginalSellerPrice permet de restaurer.
        builder.OwnsOne(o => o.CompareAtPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("compare_at_amount").HasColumnType("numeric(18,2)");
            money.Property(m => m.Currency).HasColumnName("compare_at_currency").HasMaxLength(3);
        });
        builder.Property(o => o.OriginalSellerPrice).HasColumnName("original_seller_price").HasColumnType("numeric(18,2)");
        builder.Property(o => o.DiscountEndsOnUtc).HasColumnName("discount_ends_on_utc");

        builder.Property(o => o.Condition).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.FulfillmentType).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShipFromLocationId).IsRequired();
        builder.Property(o => o.HandlingTime).IsRequired();
        builder.Property(o => o.CreatedOnUtc).IsRequired();

        builder.HasIndex(o => o.ProductId);
        builder.HasIndex(o => o.SellerId);
        builder.HasIndex(o => new { o.SellerId, o.VariantSku }).IsUnique();

        builder.Ignore(o => o.DomainEvents);
    }
}
