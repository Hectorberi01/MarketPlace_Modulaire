using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence.Configurations;

internal sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("sellers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SellerId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.ShopName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LogoUrl).HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.KybStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.CommissionRate).HasColumnType("numeric(5,4)").IsRequired();
        builder.Property(s => s.Rating).HasColumnType("numeric(3,2)").IsRequired();
        builder.Property(s => s.SalesCount).IsRequired();
        builder.Property(s => s.CreatedOnUtc).IsRequired();

        // Mémoire de la sanction. Les deux premières colonnes sont NULLABLES : elles
        // décrivent une suspension EN COURS, et la très grande majorité des lignes
        // n'en a aucune. `SuspensionCount` est requise avec un défaut à 0, ce qui
        // rend la migration applicable sans reprise de données.
        builder.Property(s => s.SuspendedOnUtc);
        builder.Property(s => s.SuspensionReason).HasMaxLength(1000);
        builder.Property(s => s.SuspensionCount).HasDefaultValue(0).IsRequired();

        // Motif du dernier refus de KYB, affiché au vendeur. Nullable : la plupart
        // des dossiers n'ont jamais été refusés.
        builder.Property(s => s.KybRejectionReason).HasMaxLength(1000);

        // Propriété calculée : elle se déduit de Status, il n'y a rien à stocker.
        builder.Ignore(s => s.IsSuspended);

        // PayoutAccount (VO nullable) sérialisé en jsonb. Cast vers le converter
        // non générique car la propriété est nullable.
        builder.Property(s => s.PayoutAccount)
            .HasConversion((ValueConverter)new PayoutAccountJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("payout_account");

        // Metadata société (VO nullable) en jsonb, null par défaut. Comme PayoutAccount,
        // le converter n'est appliqué qu'aux valeurs non nulles ; NULL reste NULL.
        builder.Property(s => s.Metadata)
            .HasConversion((ValueConverter)new SellerCompanyInfoJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("metadata");

        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasIndex(s => s.ShopName).IsUnique();

        // ═══════════════════════════════════════════════════════════════════════
        // TROIS BALAYAGES PAR AFFICHAGE, SUR UNE TABLE SANS INDEX UTILE.
        //
        // `SellerRepository.ListPagedAsync` regroupe par `Status`, regroupe par
        // `KybStatus`, compte, puis trie par `CreatedOnUtc` — et les deux seuls index
        // existants portent sur `UserId` et `ShopName`. L'écran Vendeurs et le tableau
        // de bord payaient donc trois parcours complets chacun, à chaque ouverture.
        //
        // Les deux statuts sont persistés en CHAÎNE (HasConversion<string>) : l'index
        // porte bien sur la colonne texte, celle que le regroupement lit.
        // ═══════════════════════════════════════════════════════════════════════
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.KybStatus);
        builder.HasIndex(s => s.CreatedOnUtc);

        builder.Ignore(s => s.DomainEvents);
    }
}
