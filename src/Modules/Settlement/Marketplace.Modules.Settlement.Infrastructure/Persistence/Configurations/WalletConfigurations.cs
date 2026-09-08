using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — SANS LUI, UN VENDEUR PEUT SE FAIRE VERSER 5x SON SOLDE.
        //
        // Un retrait lit le solde, le vérifie, le soustrait. Cinq requêtes en parallèle
        // lisent toutes le MÊME solde, passent toutes le contrôle, et créent cinq
        // demandes complètes. À l'approbation, cinq versements partent pour un seul
        // solde. C'est de l'argent réel, et le bug est trivial à déclencher.
        //
        // Le portefeuille plateforme a la même exigence pour une autre raison : il est
        // écrit par tous les flux à la fois (commission, frais, contre-passations), et
        // deux écritures simultanées sans verrou s'écrasent — une commission encaissée
        // disparaît des comptes.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est
        //  dépréciée et casse la build en « warnings = errors » ; notre extension fait
        //  exactement ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT.
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ. ModuleDbContext dispatche les
        // événements de domaine AVANT base.SaveChangesAsync et draine l'outbox : rejouer
        // la commande dans le MÊME scope dupliquerait les messages. On échoue franchement
        // en 409, et le client rejoue avec une requête neuve.
        //
        // LE VERROU PORTE SUR L'AGRÉGAT, PAS SUR LA LIGNE DE SOLDE. Deux mouvements
        // concurrents sur deux comptes DIFFÉRENTS du même portefeuille entrent donc en
        // conflit. C'est voulu : ces comptes se lisent ensemble (« ai-je de quoi
        // retirer ? » regarde le disponible après libération du à-venir), et un verrou
        // par compte laisserait passer précisément les courses que celui-ci ferme.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("wallets");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.OwnerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // UN SEUL PORTEFEUILLE PAR COUPLE (TYPE, PROPRIÉTAIRE) — et pas par propriétaire
        // seul : rien n'interdit qu'un même compte utilisateur soit à la fois acheteur et
        // livreur, et ce sont alors deux portefeuilles aux règles différentes.
        builder.HasIndex(w => new { w.OwnerType, w.OwnerId }).IsUnique();

        builder.HasMany(w => w.Accounts)
            .WithOne()
            .HasForeignKey("WalletId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(w => w.Accounts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WalletAccountBalanceConfiguration : IEntityTypeConfiguration<WalletAccountBalance>
{
    public void Configure(EntityTypeBuilder<WalletAccountBalance> builder)
    {
        builder.ToTable("wallet_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Account).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Balance).HasColumnType("numeric(18,2)").IsRequired();

        // LA CLÉ ÉTRANGÈRE EST DÉCLARÉE ICI, EXPLICITEMENT, ET PAS SEULEMENT DÉDUITE DE
        // LA RELATION CÔTÉ Wallet.
        //
        // « WalletId » est une propriété FANTÔME : elle n'existe pas sur la classe C#, EF
        // la fabrique à partir du HasForeignKey posé sur l'agrégat parent. Or l'index
        // ci-dessous la référence par son nom, et ApplyConfigurationsFromAssembly
        // n'ordonne pas les configurations : si celle-ci passe avant celle de Wallet, la
        // propriété n'existe pas encore et EF refuse de la créer sans type
        // (« no property type was specified »).
        //
        // La déclarer ici lève l'ambiguïté quel que soit l'ordre. C'est deux lignes, et
        // c'est un démarrage qui ne dépend plus d'un hasard de réflexion.
        builder.Property<Guid>("WalletId").IsRequired();

        // Un seul solde par compte et par portefeuille. Sans cette contrainte, un défaut
        // de code pourrait créer deux lignes « Available » sur le même portefeuille, et
        // les soldes deviendraient une question d'ordre de lecture.
        builder.HasIndex("WalletId", "Account").IsUnique();
    }
}


internal sealed class CustomerRefundConfiguration : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> builder)
    {
        builder.ToTable("customer_refunds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new CustomerRefundId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Msisdn).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProviderRef).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SentToPspAtUtc);
        builder.Property(r => r.CompletedAtUtc);

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.Status);

        builder.Ignore(r => r.DomainEvents);
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

        builder.Property(w => w.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .IsRequired();

        // Type et propriétaire recopiés depuis le portefeuille : la file admin filtre
        // dessus à chaque ouverture, une jointure par ligne coûterait plus que ces deux
        // colonnes.
        builder.Property(w => w.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.OwnerId).IsRequired();

        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.ProviderRef).HasMaxLength(200);
        builder.Property(w => w.FailureReason).HasMaxLength(500);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CompletedAtUtc);

        builder.HasIndex(w => new { w.OwnerType, w.OwnerId });

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
