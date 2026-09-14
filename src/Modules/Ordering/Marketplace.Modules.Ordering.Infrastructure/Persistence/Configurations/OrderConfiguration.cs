using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — la commande est mutée par la saga (paiement, expédition, livraison) et
        // par l'acheteur (annulation). Deux transitions concurrentes s'écraseraient.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée
        //  et casse la build en « warnings = errors » ; notre extension fait exactement
        //  ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT. Rien à changer dans le modèle de domaine.
        //
        // EF l'inclut désormais dans la clause WHERE de chaque UPDATE. Si une autre
        // transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF
        // lève `DbUpdateConcurrencyException` — traduite en 409 (voir
        // ConcurrencyExceptionHandler).
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.BuyerId).IsRequired();
        builder.Property(o => o.CartId).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        // Code promo figé au checkout (snapshot). Nullable : la plupart des commandes
        // n'en ont pas. C'est cette colonne qui permet, à la confirmation, de savoir
        // quel coupon décompter.
        builder.Property(o => o.PromotionCode).HasMaxLength(64);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.CreatedAtUtc).IsRequired();

        // NULLABLE ET SANS RÉTRO-REMPLISSAGE. Nul veut dire « pas encore livrée » — et
        // aussi « livrée avant que cette colonne existe ». Dater après coup les
        // livraisons passées reviendrait à inventer le point de départ d'un délai de
        // retour, c'est-à-dire à fermer une fenêtre sur une date fausse.
        builder.Property(o => o.DeliveredAtUtc);
        builder.Property(o => o.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalSellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalPlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ShippingFee).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.GrandTotal).HasColumnType("numeric(18,2)").IsRequired();

        // Frais de service AJOUTÉ à GrandTotal (contrairement à TaxTotal, qui en
        // décrit une part). Le taux est figé à la commande : voir l'agrégat.
        builder.Property(o => o.PlatformFee).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.PlatformFeeRate).HasColumnType("numeric(9,5)").IsRequired().HasDefaultValue(0m);

        // ─────────────────────────────────────────────────────────────────────────
        // VENTILATION DE LA TAXE. Trois colonnes qui DÉCRIVENT le total sans jamais
        // l'augmenter : régime « TVA comprise ».
        //
        // Défaut à zéro sur les deux montants : les commandes antérieures n'ont pas
        // de ventilation, et zéro raconte exactement cela — aucune taxe identifiée,
        // pas une donnée perdue. `TaxRuleId` reste nul pour la même raison.
        //
        // Le taux tient sur 5 décimales : un taux est une proportion, pas de
        // l'argent, et 0,18 comme 0,05125 doivent survivre à l'aller-retour en base
        // sans arrondi. `numeric(18,2)` aurait écrasé tout taux sous le pour-cent.
        // `NetTotal` n'est PAS stocké : c'est GrandTotal moins TaxTotal, et une
        // colonne de plus serait une troisième vérité à maintenir cohérente.
        // ─────────────────────────────────────────────────────────────────────────
        builder.Property(o => o.TaxTotal).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.TaxRate).HasColumnType("numeric(9,5)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.TaxRuleId);
        builder.Ignore(o => o.NetTotal);

        // Part réglée par la cagnotte. Valeur par défaut 0 : toutes les commandes
        // existantes ont été payées intégralement au prestataire, et c'est ce que
        // zéro raconte exactement — pas une donnée manquante.
        builder.Property(o => o.WalletApplied).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // Adresse de livraison figée (snapshot, toutes colonnes nullables).
        builder.Property(o => o.ShipToLabel).HasMaxLength(60);
        builder.Property(o => o.ShipToRecipient).HasMaxLength(120);
        builder.Property(o => o.ShipToPhone).HasMaxLength(20);
        builder.Property(o => o.ShipToCommuneCode).HasMaxLength(40);
        builder.Property(o => o.ShipToQuartier).HasMaxLength(120);
        builder.Property(o => o.ShipToLandmark).HasMaxLength(200);
        builder.Property(o => o.ShipToLine1).HasMaxLength(200);
        builder.Property(o => o.ShipToCountryCode).HasMaxLength(2);
        builder.Property(o => o.ShipToLatitude);
        builder.Property(o => o.ShipToLongitude);

        // Résolus depuis BeninGeography à l'affichage : rien à stocker.
        builder.Ignore(o => o.ShipToCommuneName);
        builder.Ignore(o => o.HasShipToCoordinates);

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
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => new { o.BuyerId, o.Status });

        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OfferId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.SellerId).IsRequired();
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();
        builder.Property(l => l.ShipFromLocationId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitBasePrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.SellerDiscount).HasColumnType("numeric(18,2)").IsRequired();

        // Barème figé sur la ligne. numeric(6,4) : exact, et large au-delà du plausible.
        builder.Property(l => l.CommissionRate).HasColumnType("numeric(6,4)").IsRequired();
        builder.Property(l => l.ProviderFeeRate).HasColumnType("numeric(6,4)").IsRequired();
        builder.Property(l => l.PlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();

        // ─────────────────────────────────────────────────────────────────────────
        // LES DEUX COLONNES ONT ÉCHANGÉ LEUR RÔLE.
        //
        // `LineTotal` était calculé (`FinalUnitPrice * Quantity`) et `FinalUnitPrice`
        // stocké. C'est l'inverse désormais : le montant de LIGNE est la valeur de
        // référence, arrondie une fois, et le prix unitaire un affichage déduit.
        //
        // La raison est une remise de panier : « −500 sur votre commande » n'a pas
        // d'équivalent unitaire, et la diviser par la quantité crée un arrondi par
        // article — donc un écart entre le total affiché à l'acheteur et le total
        // facturé, que le contrôle `expectedTotal` transformerait en refus de paiement.
        //
        // MIGRATION AVEC REPRISE DE DONNÉES : la colonne `LineTotal` doit être
        // alimentée depuis `FinalUnitPrice * Quantity`, et les deux colonnes de remise
        // multipliées par la quantité — elles étaient unitaires. Sans cette reprise,
        // toutes les commandes existantes deviennent fausses.
        // ─────────────────────────────────────────────────────────────────────────
        builder.Property(l => l.LineTotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Ignore(l => l.FinalUnitPrice);

        builder.HasIndex("OrderId");
        builder.HasIndex(l => l.SellerId);
    }
}
