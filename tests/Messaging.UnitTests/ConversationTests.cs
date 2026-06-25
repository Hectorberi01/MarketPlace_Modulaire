using FluentAssertions;
using Marketplace.Modules.Messaging.Domain.Conversations;
using Xunit;

namespace Messaging.UnitTests;

public class ConversationTests
{
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid Seller = Guid.NewGuid();

    private static Conversation NewConversation()
        => Conversation.Start(new[] { Buyer, Seller }, "Product", Guid.NewGuid(), Buyer, "Bonjour, est-ce disponible ?").Value;

    [Fact]
    public void Start_creates_open_conversation_with_initial_message()
    {
        var conversation = NewConversation();

        conversation.Status.Should().Be(ConversationStatus.Open);
        conversation.Participants.Should().HaveCount(2);
        conversation.Messages.Should().HaveCount(1);
        conversation.ParticipantIds.Should().Contain(new[] { Buyer, Seller });
    }

    [Fact]
    public void Start_requires_two_participants()
        => Conversation.Start(new[] { Buyer }, null, null, Buyer, "x").IsFailure.Should().BeTrue();

    [Fact]
    public void Non_participant_cannot_send()
    {
        var conversation = NewConversation();

        conversation.SendMessage(Guid.NewGuid(), "intrusion", Array.Empty<string>()).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void MarkRead_marks_messages_from_others()
    {
        var conversation = NewConversation(); // initial message from Buyer
        conversation.SendMessage(Seller, "Oui c'est dispo", Array.Empty<string>());

        conversation.MarkRead(Buyer); // Buyer reads -> Seller's message marked read

        conversation.Messages.Single(m => m.SenderId == Seller).ReadAtUtc.Should().NotBeNull();
        conversation.Messages.Single(m => m.SenderId == Buyer).ReadAtUtc.Should().BeNull();
    }
}
