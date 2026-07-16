using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence.Configurations;

internal sealed class SellerWalletConfiguration : IEntityTypeConfiguration<SellerWallet>
{
    public void Configure(EntityTypeBuilder<SellerWallet> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — SANS LUI, UN VENDEUR PEUT SE FAIRE VERSER 5× SON SOLDE.
        //
        // `Withdraw()` lit le solde, le vérifie, le soustrait. Cinq requêtes de retrait
        // en parallèle lisent toutes le MÊME solde, passent toutes le contrôle, et
        // créent CINQ demandes complètes. À l'approbation, cinq versements partent pour
        // un seul solde. C'est de l'argent réel, et le bug est trivial à déclencher.
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
        // ⚠️ AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("seller_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new SellerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.PendingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.HasIndex(w => w.SellerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class PlatformWalletConfiguration : IEntityTypeConfiguration<PlatformWallet>
{
    public void Configure(EntityTypeBuilder<PlatformWallet> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — le portefeuille plateforme est écrit par tous les flux (commission,
        // frais, contre-passations). Sans verrou, deux écritures simultanées s'écrasent :
        // une commission encaissée disparaît des comptes.
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
        // ⚠️ AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("platform_wallet");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.CommissionBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ProviderFeeBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ShippingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("withdrawals");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.ProviderRef).HasMaxLength(200);
        builder.Property(w => w.FailureReason).HasMaxLength(500);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CompletedAtUtc);

        builder.HasIndex(w => w.SellerId);

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Account).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Reason).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ReferenceType).HasMaxLength(30);
        builder.Property(t => t.ReferenceId);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.HasIndex(t => new { t.OwnerId, t.CreatedAtUtc });

        // ─────────────────────────────────────────────────────────────────────────
        // INDEX D'IDEMPOTENCE — la seule garantie qui tienne sous concurrence.
        //
        // Le handler de contre-passation vérifie d'abord « ce remboursement a-t-il déjà
        // été passé ? ». Mais entre la lecture et l'écriture, deux rejeux simultanés
        // peuvent tous deux se croire les premiers. Une vérification applicative ne
        // ferme JAMAIS complètement cette fenêtre : seule la base le peut.
        //
        // L'unicité porte sur (référence, propriétaire, compte) : un remboursement
        // produit au plus une écriture par compte et par propriétaire — solde à venir
        // et solde principal du vendeur, commission et frais de la plateforme. Un
        // doublon lève une violation de contrainte, la transaction est annulée, et le
        // vendeur n'est pas débité deux fois.
        //
        // L'index est PARTIEL (`HasFilter`) : il ne s'applique qu'aux écritures de
        // remboursement. Les lignes de commande, elles, sont légitimement multiples
        // pour une même référence.
        //
        // ⚠️ REQUIERT UNE MIGRATION :
        //    dotnet ef migrations add AddRefundReversalIdempotencyIndex \
        //      -p src/Modules/Settlement/Marketplace.Modules.Settlement.Infrastructure
        // ─────────────────────────────────────────────────────────────────────────
        // ⚠️ Le nom de colonne du filtre est en PascalCase et entre guillemets doubles :
        // ce projet n'applique AUCUNE convention snake_case (vérifié dans le
        // ModelSnapshot). Écrire `reference_type` produirait un index invalide, et
        // l'erreur ne surgirait qu'au moment d'appliquer la migration.
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId, t.OwnerId, t.Account })
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'refund'")
            .HasDatabaseName("ux_wallet_transactions_refund_reversal");

        builder.Ignore(t => t.DomainEvents);
    }
}
