using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Cart.Domain.Carts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<CartAggregate>
{
    public void Configure(EntityTypeBuilder<CartAggregate> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.BuyerId).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Code promo du panier. Nullable : la plupart des paniers n'en ont pas.
        builder.Property(c => c.PromotionCode).HasMaxLength(64);

        // IsRequired() est indispensable : sans lui, EF considère la relation
        // optionnelle et, à la suppression d'une ligne, tente un
        // « UPDATE cart_items SET CartId = NULL » (sévérance) au lieu d'un DELETE.
        // Comme la colonne CartId est NOT NULL, Postgres rejette → 500. Avec
        // IsRequired(), EF supprime l'orphelin (DELETE), ce qui est le comportement
        // attendu. Aligné sur le snapshot/migration (pas de nouvelle migration).
        //
        // ⚠️ NOTE DE 2026-07-14 (voir doc 22) : ce commentaire est ANTÉRIEUR et je n'ai pas
        // pu le vérifier. En EF Core, le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne AUSSI le sort des orphelins — avec Cascade, un enfant retiré d'une
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production le confirment
        // sur les autres agrégats (10 commandes confirmées, zéro réservation orpheline).
        //
        // Le 500 décrit ci-dessus a donc probablement une autre cause, ou s'est produit
        // AVANT que `OnDelete(Cascade)` ne soit configuré. Je ne le supprime pas — il
        // consigne peut-être un incident réel — mais ne le prenez pas pour une explication
        // établie.
        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey("CartId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.BuyerId, c.Status });

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.OfferId).IsRequired();
        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.CategoryId).IsRequired();
        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ShipFromLocationId).IsRequired();
        builder.Property(i => i.UnitBaseAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasIndex("CartId", "OfferId").IsUnique();
    }
}
