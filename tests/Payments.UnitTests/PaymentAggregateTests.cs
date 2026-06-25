using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Payments.Domain.Payments;
using Xunit;

namespace Payments.UnitTests;

public class PaymentAggregateTests
{
    private static Payment NewPayment()
        => Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(5100m, "XOF").Value, PaymentMethod.MobileMoney, "MTN MoMo", PaymentFlow.HostedCheckout).Value;

    [Fact]
    public void Create_starts_pending_and_raises_initiated()
    {
        var payment = NewPayment();

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.Amount.Amount.Should().Be(5100m);
    }

    [Fact]
    public void Capture_gele_les_fonds_en_escrow_jusqu_a_liberation()
    {
        var payment = NewPayment();
        payment.Capture("MTN-TX-123");

        payment.IsEscrowHeld.Should().BeTrue();
        payment.EscrowReleasedAt.Should().BeNull();

        payment.ReleaseEscrow().IsSuccess.Should().BeTrue();
        payment.EscrowReleasedAt.Should().NotBeNull();
        payment.IsEscrowHeld.Should().BeFalse();

        var firstReleasedAt = payment.EscrowReleasedAt;
        payment.ReleaseEscrow(); // idempotent
        payment.EscrowReleasedAt.Should().Be(firstReleasedAt);
    }

    [Fact]
    public void ReleaseEscrow_refuse_si_pas_encaisse()
        => NewPayment().ReleaseEscrow().IsFailure.Should().BeTrue();

    [Fact]
    public void Capture_moves_to_captured_and_stores_reference()
    {
        var payment = NewPayment();

        var result = payment.Capture("MTN-TX-123");

        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Captured);
        payment.ProviderReference.Should().Be("MTN-TX-123");
        payment.CapturedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Captured_payment_cannot_fail()
    {
        var payment = NewPayment();
        payment.Capture("ref");

        payment.Fail("late").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Refund_requires_capture_first()
    {
        var payment = NewPayment();

        payment.Refund().IsFailure.Should().BeTrue();

        payment.Capture("ref");
        payment.Refund().IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void Create_with_zero_amount_fails()
    {
        var result = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Zero("XOF"), PaymentMethod.Card, "Visa", PaymentFlow.HostedCheckout);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("payments.amount_invalid");
    }
}
