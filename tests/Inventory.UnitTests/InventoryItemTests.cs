using FluentAssertions;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;
using Xunit;

namespace Inventory.UnitTests;

public class InventoryItemTests
{
    private static InventoryItem NewItem(int onHand = 10, int threshold = 2)
        => InventoryItem.Create(Sku.Create("SKU-1").Value, Guid.NewGuid(), onHand, threshold).Value;

    [Fact]
    public void Available_est_OnHand_moins_Reserved()
    {
        var item = NewItem(onHand: 10);
        item.Reserve(Guid.NewGuid(), 3, DateTime.UtcNow.AddMinutes(15));

        item.Reserved.Should().Be(3);
        item.Available.Should().Be(7);
    }

    [Fact]
    public void Reserve_refuse_au_dela_du_disponible()
    {
        var item = NewItem(onHand: 5);

        var result = item.Reserve(Guid.NewGuid(), 6, DateTime.UtcNow.AddMinutes(15));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("inventory.item.insufficient_stock");
    }

    [Fact]
    public void Confirm_decremente_le_stock_physique()
    {
        var item = NewItem(onHand: 10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 4, DateTime.UtcNow.AddMinutes(15));

        item.ConfirmReservation(orderId).IsSuccess.Should().BeTrue();

        item.OnHand.Should().Be(6);
        item.Reserved.Should().Be(0);
        item.Available.Should().Be(6);
    }

    [Fact]
    public void Release_rend_le_stock_disponible()
    {
        var item = NewItem(onHand: 10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 4, DateTime.UtcNow.AddMinutes(15));

        item.ReleaseReservation(orderId);

        item.Reserved.Should().Be(0);
        item.Available.Should().Be(10);
    }

    [Fact]
    public void IsLowStock_quand_disponible_sous_le_seuil()
    {
        var item = NewItem(onHand: 3, threshold: 5);
        item.IsLowStock.Should().BeTrue();
    }
}
