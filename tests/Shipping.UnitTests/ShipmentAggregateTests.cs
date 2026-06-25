using FluentAssertions;
using Marketplace.Modules.Shipping.Domain.Shipments;
using Xunit;

namespace Shipping.UnitTests;

public class ShipmentAggregateTests
{
    private static Shipment NewShipment()
        => Shipment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ShipmentLineDraft("SKU-1", 2) }).Value;

    [Fact]
    public void Create_starts_pending_with_items()
    {
        var shipment = NewShipment();

        shipment.Status.Should().Be(ShipmentStatus.Pending);
        shipment.Items.Should().HaveCount(1);
        shipment.Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public void Create_without_lines_fails()
    {
        var result = Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Array.Empty<ShipmentLineDraft>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.no_lines");
    }

    [Fact]
    public void Happy_path_prepare_ship_deliver()
    {
        var shipment = NewShipment();

        shipment.MarkPreparing().IsSuccess.Should().BeTrue();
        shipment.MarkShipped("DHL", "TRACK-123").IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Shipped);
        shipment.TrackingNumber.Should().Be("TRACK-123");

        shipment.MarkDelivered().IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
    }

    [Fact]
    public void Ship_requires_carrier_and_tracking()
    {
        var shipment = NewShipment();

        shipment.MarkShipped("", "").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Delivered_shipment_cannot_be_cancelled()
    {
        var shipment = NewShipment();
        shipment.MarkShipped("DHL", "T1");
        shipment.MarkDelivered();

        shipment.Cancel().IsFailure.Should().BeTrue();
    }
}
