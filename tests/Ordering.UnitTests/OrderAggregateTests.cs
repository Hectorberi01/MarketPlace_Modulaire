using FluentAssertions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Xunit;

namespace Ordering.UnitTests;

public class OrderAggregateTests
{
    private static OrderLineDraft Line(int qty, decimal unitBase, decimal sellerDisc, decimal platformDisc, decimal finalUnit)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-1", Guid.NewGuid(), qty, unitBase, sellerDisc, platformDisc, finalUnit);

    private static Order NewOrder(params OrderLineDraft[] lines)
        => Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", lines).Value;

    [Fact]
    public void Create_computes_totals_from_lines()
    {
        // 2 × (base 7000, -1400 vendeur, -500 plateforme, final 5100) → totaux ×2.
        var order = NewOrder(Line(2, 7000m, 1400m, 500m, 5100m));

        order.Subtotal.Should().Be(14000m);
        order.TotalSellerDiscount.Should().Be(2800m);
        order.TotalPlatformDiscount.Should().Be(1000m);
        order.GrandTotal.Should().Be(10200m);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void MarkDelivered_depuis_confirmed_passe_en_livre()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();
        order.MarkPaid();
        order.Confirm();

        order.MarkDelivered().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void MarkDelivered_refuse_si_pas_confirmee()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.MarkDelivered().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_with_no_lines_fails()
    {
        var result = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", Array.Empty<OrderLineDraft>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ordering.no_lines");
    }

    [Fact]
    public void Happy_path_transitions_to_confirmed()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));

        order.MarkAwaitingPayment().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.AwaitingPayment);

        order.MarkPaid().IsSuccess.Should().BeTrue();
        order.Confirm().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Confirm_before_payment_is_rejected()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.Confirm().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Confirmed_order_cannot_be_cancelled()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();
        order.MarkPaid();
        order.Confirm();

        order.Cancel("test").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Place_then_cancel_releases_state()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.Cancel("changed mind").IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }
}
