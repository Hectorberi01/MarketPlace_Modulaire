using FluentAssertions;
using Marketplace.Modules.Returns.Domain.Returns;
using Xunit;

namespace Returns.UnitTests;

public class ReturnRequestTests
{
    private static ReturnRequest NewRequest()
        => ReturnRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReturnReason.Defective, "XOF").Value;

    [Fact]
    public void Created_request_is_requested()
        => NewRequest().Status.Should().Be(ReturnStatus.Requested);

    [Fact]
    public void Happy_path_approve_receive_refund()
    {
        var request = NewRequest();

        request.Approve().IsSuccess.Should().BeTrue();
        request.AddReturnTracking("DHL", "RET-1").IsSuccess.Should().BeTrue();
        request.MarkReceived().IsSuccess.Should().BeTrue();
        request.Refund(5100m).IsSuccess.Should().BeTrue();

        request.Status.Should().Be(ReturnStatus.Refunded);
        request.RefundAmount.Should().Be(5100m);
        request.RefundId.Should().NotBeNull();
    }

    [Fact]
    public void Refund_before_received_is_rejected()
    {
        var request = NewRequest();
        request.Approve();

        request.Refund(100m).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Rejected_request_cannot_be_approved()
    {
        var request = NewRequest();
        request.Reject("hors délai");

        request.Approve().IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Rejected);
    }
}
