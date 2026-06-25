using Microsoft.EntityFrameworkCore;
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

        builder.HasMany(c => c.Participants)
            .WithOne()
            .HasForeignKey("ConversationId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey("ConversationId")
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

        // Pièces jointes en text[] natif (stockage uniquement, jamais filtré).
        builder.Property<List<string>>("_attachments")
            .HasColumnName("attachments")
            .HasColumnType("text[]")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
        builder.Ignore(m => m.Attachments);
    }
}
