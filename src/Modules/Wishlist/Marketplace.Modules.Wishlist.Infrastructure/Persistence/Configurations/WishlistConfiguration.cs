using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Wishlist.Domain.Wishlists;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Marketplace.Modules.Wishlist.Infrastructure.Persistence.Configurations;

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<WishlistAggregate>
{
    public void Configure(EntityTypeBuilder<WishlistAggregate> builder)
    {
        builder.ToTable("wishlists");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WishlistId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.UserId).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // ⚠️ CORRECTION D'UNE AFFIRMATION ANTÉRIEURE (voir doc 22). Ce commentaire disait
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
        builder.HasMany(w => w.Items)
            .WithOne()
            .HasForeignKey("WishlistId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Une seule liste d'envies par utilisateur.
        builder.HasIndex(w => w.UserId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("wishlist_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.OfferId);
        builder.Property(i => i.PriceAlert).IsRequired();
        builder.Property(i => i.StockAlert).IsRequired();
        builder.Property(i => i.AddedAtUtc).IsRequired();

        builder.HasIndex(i => i.ProductId);
    }
}
