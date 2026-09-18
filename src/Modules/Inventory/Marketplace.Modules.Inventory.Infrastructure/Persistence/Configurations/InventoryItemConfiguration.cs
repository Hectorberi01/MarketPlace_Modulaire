using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — SURVENTE.
        //
        // `Reserve()` vérifie `Available >= quantity` puis réserve. Deux commandes
        // simultanées sur le dernier article passent toutes deux le contrôle : on vend
        // deux fois un stock unique, et un client attend un colis qui n'existe pas.
        //
        // ICI, LE JETON SEUL NE SUFFISAIT PAS — ET C'EST LE PIÈGE DE CET AGRÉGAT.
        //
        // `Reserve()` et `ReleaseReservation()` ne modifient aucune colonne de CETTE
        // table : ils insèrent ou suppriment des lignes ENFANTS (stock_reservations).
        // EF n'émet alors AUCUN `UPDATE inventory_items` — or un jeton de concurrence
        // n'est évalué que dans la clause WHERE d'un UPDATE. Le verrou aurait donc été
        // posé, visible, commenté… et parfaitement inerte sur le seul chemin qui vend.
        //
        // D'où `StockVersion` : une colonne que TOUTE mutation incrémente (voir
        // InventoryItem.Touch). Elle salit la ligne parente, EF émet l'UPDATE, et le
        // `xmin` est enfin confronté. La valeur n'est lue par personne — c'est le fait
        // de l'écrire qui protège.
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
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InventoryItemId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(i => i.LocationId).IsRequired();
        builder.Property(i => i.OnHand).IsRequired();
        builder.Property(i => i.ReorderThreshold).IsRequired();

        // Le compteur qui rend le verrou effectif (voir l'encadré en tête de méthode).
        // Valeur par défaut 0 : les lignes déjà en base démarrent à 0 sans migration de données.
        builder.Property(i => i.StockVersion).IsRequired().HasDefaultValue(0);

        // Reserved et Available sont calculés à partir des réservations.
        builder.Ignore(i => i.Reserved);
        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);

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
        builder.HasMany(i => i.Reservations)
            .WithOne()
            .HasForeignKey("InventoryItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Sku);
        builder.HasIndex(i => new { i.Sku, i.LocationId }).IsUnique();

        builder.Ignore(i => i.DomainEvents);
    }
}
