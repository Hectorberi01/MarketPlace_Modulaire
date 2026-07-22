using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Shipping.Domain.Shipments;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new ShipmentId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.OrderId).IsRequired();
        builder.Property(s => s.SellerId).IsRequired();
        builder.Property(s => s.BuyerId).IsRequired();
        builder.Property(s => s.ShipFromLocationId).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Carrier).HasMaxLength(100);
        builder.Property(s => s.TrackingNumber).HasMaxLength(100);
        builder.Property(s => s.TrackingUrl).HasMaxLength(300);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.ShippedAtUtc);
        builder.Property(s => s.DeliveredAtUtc);

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
        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey("ShipmentId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => s.OrderId);
        builder.HasIndex(s => s.SellerId);

        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> builder)
    {
        builder.ToTable("shipment_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasIndex("ShipmentId");
    }
}
