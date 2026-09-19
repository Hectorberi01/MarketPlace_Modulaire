using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Marketing.Domain.Legal;

namespace Marketplace.Modules.Marketing.Infrastructure.Persistence.Configurations;

internal sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new LegalDocumentId(value))
            .ValueGeneratedNever();

        // Enum EN TEXTE, comme partout ailleurs dans ce dépôt : une colonne qui se lit
        // sans table de correspondance, et dont l'insertion d'une valeur au milieu de
        // l'énumération ne décale pas le sens des lignes déjà écrites.
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(d => d.Slug).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Conversion écrite EN TOUTES LETTRES des deux côtés, type fournisseur `Guid?`.
        //
        // CORRECTION D'UNE AFFIRMATION QUE J'AI D'ABORD ÉCRITE ICI : non, la forme
        // courte (`id => id!.Value.Value`) n'aurait PAS échoué. Un convertisseur déclaré
        // par `HasConversion` a `convertsNulls: false`, et EF court-circuite alors les
        // valeurs nulles sans jamais l'appeler ; la nullabilité de la colonne vient de
        // `IProperty.IsNullable`, pas du type fournisseur. Les deux formes marchent.
        //
        // Celle-ci est gardée parce qu'elle se lit sans connaître cette règle-là.
        builder.Property(d => d.CurrentVersionId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new LegalDocumentVersionId(value.Value) : (LegalDocumentVersionId?)null);

        // L'adresse est ce par quoi les surfaces demandent le document. UNIQUE EN BASE,
        // pas seulement vérifié par le handler : deux lignes de même adresse rendraient
        // la réponse dépendante de l'ordre de lecture, et le handler ne peut rien contre
        // deux créations simultanées.
        builder.HasIndex(d => d.Slug).IsUnique();

        // Les versions se chargent avec leur document : l'agrégat ne défend ses
        // invariants qu'en les voyant toutes. La relation est déclarée du côté enfant,
        // pour que la clé étrangère existe avant d'être indexée.

        // ═══════════════════════════════════════════════════════════════════════════
        // `Draft` EST UNE PROPRIÉTÉ CALCULÉE, ET EF LA PRENDRAIT POUR UNE NAVIGATION.
        //
        // Elle renvoie un `LegalDocumentVersion?` : par convention, EF y verrait une
        // seconde relation vers la même table et fabriquerait une clé étrangère ombre
        // `DraftId` — une colonne que personne n'écrit, sur une relation qui n'existe
        // pas. Le brouillon n'est pas un lien, c'est une LECTURE de la collection.
        //
        // Même raison que `Campaign.PromotionIds` et `User.RoleIds` juste à côté.
        // ═══════════════════════════════════════════════════════════════════════════
        builder.Ignore(d => d.Draft);

        builder.Ignore(d => d.DomainEvents);
    }
}

internal sealed class LegalDocumentVersionConfiguration : IEntityTypeConfiguration<LegalDocumentVersion>
{
    public void Configure(EntityTypeBuilder<LegalDocumentVersion> builder)
    {
        builder.ToTable("legal_document_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasConversion(id => id.Value, value => new LegalDocumentVersionId(value))
            .ValueGeneratedNever();

        builder.Property(v => v.DocumentId)
            .HasConversion(id => id.Value, value => new LegalDocumentId(value))
            .IsRequired();

        // ═══════════════════════════════════════════════════════════════════════════
        // LE NUMÉRO EST STOCKÉ EN TEXTE, ET CE N'EST PAS UN RENONCEMENT.
        //
        // « 1.10 » est POSTÉRIEUR à « 1.9 », mais lui est INFÉRIEUR en tri alphabétique.
        // Aucune requête ne doit donc ordonner sur cette colonne : la comparaison se fait
        // dans le domaine, sur les deux entiers, et c'est lui qui refuse une publication
        // qui reculerait. La colonne sert à lire et à retrouver, jamais à classer.
        //
        // C'est aussi pourquoi les listes trient sur la DATE DE CRÉATION et non sur elle.
        // ═══════════════════════════════════════════════════════════════════════════
        builder.Property(v => v.Number)
            .HasConversion(
                number => number.ToString(),
                value => LegalVersionNumber.Relire(value))
            .HasColumnName("Version")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.Content).HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.EffectiveFromUtc);
        builder.Property(v => v.PublishedOnUtc);
        builder.Property(v => v.PublishedBy);
        builder.Property(v => v.CreatedOnUtc).IsRequired();

        builder.HasOne<LegalDocument>()
            .WithMany(d => d.Versions)
            .HasForeignKey(v => v.DocumentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Un numéro n'est jamais réemployé DANS un document — la garantie que l'agrégat
        // défend déjà, posée ici en dur pour qu'aucune écriture concurrente ne la
        // contourne. C'est l'invariant qui donne son sens à toute la table : sans lui,
        // deux textes différents peuvent porter le même numéro, et une acceptation ne
        // désigne plus rien.
        builder.HasIndex(v => new { v.DocumentId, v.Number }).IsUnique();

        builder.HasIndex(v => new { v.DocumentId, v.Status });
    }
}

internal sealed class LegalAcceptanceConfiguration : IEntityTypeConfiguration<LegalAcceptance>
{
    public void Configure(EntityTypeBuilder<LegalAcceptance> builder)
    {
        builder.ToTable("legal_acceptances");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new LegalAcceptanceId(value))
            .ValueGeneratedNever();

        // PAS DE CLÉ ÉTRANGÈRE vers identity.users : la règle du dépôt est qu'un module
        // ne référence que son propre schéma. C'est un identifiant nu, et c'est assumé.
        builder.Property(a => a.UserId).IsRequired();

        builder.Property(a => a.DocumentId)
            .HasConversion(id => id.Value, value => new LegalDocumentId(value))
            .IsRequired();

        builder.Property(a => a.VersionId)
            .HasConversion(id => id.Value, value => new LegalDocumentVersionId(value))
            .IsRequired();

        builder.Property(a => a.AcceptedOnUtc).IsRequired();
        builder.Property(a => a.Context).HasMaxLength(60).IsRequired();

        // UNE SEULE LIGNE PAR PERSONNE ET PAR VERSION. La garde d'idempotence du handler
        // lit avant d'écrire, ce qui laisse passer deux appels simultanés — un double clic
        // sur « J'accepte » suffit. L'index tranche, et la date d'origine est préservée.
        builder.HasIndex(a => new { a.UserId, a.VersionId }).IsUnique();

        // « Qui a accepté cette version ? » est la question du jour du litige.
        builder.HasIndex(a => a.VersionId);

        builder.Ignore(a => a.DomainEvents);
    }
}
