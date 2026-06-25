using FluentAssertions;
using Marketplace.Modules.Disputes.Domain.Disputes;
using Xunit;

namespace Disputes.UnitTests;

public class DisputeAggregateTests
{
    private static Dispute NewDispute()
        => Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.NotReceived, "Je n'ai jamais reçu le colis.", null).Value;

    [Fact]
    public void Opened_dispute_has_initial_message_and_is_open()
    {
        var dispute = NewDispute();

        dispute.Status.Should().Be(DisputeStatus.Open);
        dispute.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void Review_then_resolve_sets_resolution()
    {
        var dispute = NewDispute();

        dispute.StartReview().IsSuccess.Should().BeTrue();
        dispute.Status.Should().Be(DisputeStatus.UnderReview);

        dispute.Resolve(DisputeResolution.RefundBuyer).IsSuccess.Should().BeTrue();
        dispute.Status.Should().Be(DisputeStatus.Resolved);
        dispute.Resolution.Should().Be(DisputeResolution.RefundBuyer);
    }

    [Fact]
    public void Resolved_dispute_rejects_new_messages()
    {
        var dispute = NewDispute();
        dispute.Resolve(DisputeResolution.ReleaseToSeller);

        dispute.AddMessage(Guid.NewGuid(), "encore un mot", null).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Open_without_message_fails()
        => Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.Other, "  ", null).IsFailure.Should().BeTrue();
}
