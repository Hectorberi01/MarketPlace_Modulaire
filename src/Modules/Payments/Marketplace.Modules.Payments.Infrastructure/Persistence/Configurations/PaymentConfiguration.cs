using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Payments.Domain.Payments;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Payments.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — DOUBLE CAPTURE = DOUBLE CRÉDIT AU VENDEUR.
        //
        // `Capture()` vérifie que le statut est Pending/Authorized. Mais deux webhooks
        // FedaPay concurrents (le PSP RÉESSAIE en cas de timeout) lisent tous deux
        // « Pending », passent tous deux, et émettent DEUX FOIS PaymentCaptured — donc
        // deux crédits pour un seul paiement.
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
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OrderId).IsRequired();
        builder.Property(p => p.BuyerId).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Flow).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.CapturedAtUtc);
        builder.Property(p => p.EscrowReleasedAt);
        builder.Ignore(p => p.IsEscrowHeld);

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(p => p.Amount).IsRequired();

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ProviderReference);

        // Le camembert « Paiements par statut » porte désormais une fenêtre, et le tri
        // par défaut des listes paginées est déjà chronologique : les deux filtrent sur
        // cette colonne, qui n'était pas indexée.
        builder.HasIndex(p => p.CreatedAtUtc);

        builder.Ignore(p => p.DomainEvents);
    }
}
