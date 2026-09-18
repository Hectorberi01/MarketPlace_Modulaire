using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Infrastructure.Persistence.Configurations;

internal sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("disputes");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DisputeId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.OrderId).IsRequired();
        builder.Property(d => d.RaisedBy).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Resolution).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.RefundAmount).HasPrecision(18, 2);
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.ResolvedAtUtc);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // CORRECTION D'UNE AFFIRMATION ANTÉRIEURE (voir doc 22). Ce commentaire disait
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
        builder.HasMany(d => d.Messages)
            .WithOne()
            .HasForeignKey("DisputeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.OrderId);
        builder.HasIndex(d => d.RaisedBy);

        builder.Ignore(d => d.DomainEvents);
    }
}

internal sealed class DisputeMessageConfiguration : IEntityTypeConfiguration<DisputeMessage>
{
    public void Configure(EntityTypeBuilder<DisputeMessage> builder)
    {
        builder.ToTable("dispute_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.AuthorId).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.PhotoUrl).HasMaxLength(500);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.HasIndex("DisputeId");
    }
}
