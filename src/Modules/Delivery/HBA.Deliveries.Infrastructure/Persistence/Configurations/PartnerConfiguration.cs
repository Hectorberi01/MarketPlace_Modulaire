using HBA.Deliveries.Domain.Partners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PartnerId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DailyQuota).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        builder.Property(p => p.WebhookUrl).HasMaxLength(500);

        // ─────────────────────────────────────────────────────────────────────
        // LE SECRET DE WEBHOOK EST STOCKÉ EN CLAIR — ET IL LE DOIT.
        //
        // Contrairement à une clé d'API, il n'est pas PRÉSENTÉ par le partenaire :
        // c'est nous qui l'utilisons pour signer chaque appel sortant. Un
        // condensat serait inutilisable — on ne peut pas signer avec une empreinte.
        //
        // La protection est donc ailleurs : accès restreint à la table, et
        // rotation possible à tout moment via ConfigureWebhook. C'est le même
        // compromis que pour un secret de webhook PSP.
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(p => p.WebhookSecret).HasMaxLength(200);

        builder.OwnsMany(p => p.ApiKeys, key =>
        {
            key.ToTable("partner_api_keys");
            key.WithOwner().HasForeignKey("partner_id");
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).ValueGeneratedNever();

            key.Property(k => k.Prefix).HasMaxLength(24).IsRequired();
            key.Property(k => k.Hash).HasMaxLength(64).IsRequired();
            key.Property(k => k.Label).HasMaxLength(120);
            key.Property(k => k.CreatedAtUtc).IsRequired();
            key.Property(k => k.RevokedAtUtc);
            key.Property(k => k.LastUsedAtUtc);

            // ─────────────────────────────────────────────────────────────────
            // L'INDEX QUI REND L'AUTHENTIFICATION VIABLE.
            //
            // Il s'exécute à CHAQUE appel partenaire. Sans lui, authentifier une
            // requête exigerait de parcourir toutes les clés de toutes les lignes
            // et de comparer les condensats un par un — un balayage complet, sur
            // le chemin le plus chaud de l'API publique.
            //
            // UNIQUE, et pas seulement indexé : deux clés actives partageant un
            // préfixe rendraient l'authentification ambiguë. Le préfixe fait
            // douze caractères tirés d'un secret de 256 bits ; la collision est
            // théorique, mais la contrainte coûte zéro et supprime la question.
            // ─────────────────────────────────────────────────────────────────
            key.HasIndex(k => k.Prefix)
                .IsUnique()
                .HasDatabaseName("ux_partner_api_keys_prefix");
        });

        builder.HasIndex(p => p.Status).HasDatabaseName("ix_partners_status");
    }
}
