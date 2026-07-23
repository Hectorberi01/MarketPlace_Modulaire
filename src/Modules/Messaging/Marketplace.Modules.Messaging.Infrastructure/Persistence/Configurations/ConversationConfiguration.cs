using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new ConversationId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.ContextType).HasMaxLength(50);
        builder.Property(c => c.ContextId);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.LastMessageAtUtc).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // ⚠️ Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
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
        builder.HasMany(c => c.Participants)
            .WithOne()
            .HasForeignKey("ConversationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // ⚠️ Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
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
        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey("ConversationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.ParticipantIds);
        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.UserId).IsRequired();

        // Index sur le participant pour la requête « mes conversations ».
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("conversation_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.SenderId).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.ReadAtUtc);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        // Suppression « pour tout le monde » : le corps N'EST PAS effacé (preuve/support),
        // seule cette date est posée. La projection se charge de masquer le contenu.
        builder.Property(m => m.DeletedAtUtc);

        // Pièces jointes en text[] natif (stockage uniquement, jamais filtré).
        //
        // ⚠️ EF Core 8 traite par défaut un `List<string>` comme une « collection
        // primitive » sérialisée en JSON — et cet aller-retour JSON contre une colonne
        // `text[]` revient VIDE en silence (le message se crée, mais la pièce jointe
        // disparaît). On force donc le mapping tableau NATIF de Npgsql via une
        // conversion `List<string>` ↔ `string[]`, avec le comparateur de valeurs requis
        // pour le suivi des modifications d'une collection.
        builder.Property<List<string>>("_attachments")
            .HasColumnName("attachments")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => v.ToList(),
                new ValueComparer<List<string>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
                    v => v.ToList()))
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Ignore(m => m.Attachments);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // ⚠️ Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
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
        builder.HasMany(m => m.Reactions)
            .WithOne()
            .HasForeignKey("MessageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Reactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // ⚠️ Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
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
        builder.HasMany(m => m.HiddenFor)
            .WithOne()
            .HasForeignKey("MessageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.HiddenFor).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.IsDeleted);
    }
}

internal sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("message_reactions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.UserId).IsRequired();
        // 16 caractères : un emoji peut être composé (séquences ZWJ, sélecteurs de variante).
        builder.Property(r => r.Emoji).HasMaxLength(16).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // Invariant DB : une seule réaction par personne et par message. Le domaine
        // l'assure déjà, mais on le grave aussi en base (défense en profondeur).
        builder.HasIndex("MessageId", "UserId").IsUnique();
    }
}

internal sealed class MessageHiddenForConfiguration : IEntityTypeConfiguration<MessageHiddenFor>
{
    public void Configure(EntityTypeBuilder<MessageHiddenFor> builder)
    {
        builder.ToTable("message_hidden_for");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.HiddenAtUtc).IsRequired();

        // Un message ne peut être masqué qu'une fois par utilisateur.
        builder.HasIndex("MessageId", "UserId").IsUnique();
    }
}
