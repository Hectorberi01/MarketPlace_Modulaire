using FluentAssertions;
using Xunit;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Wishlist.UnitTests;

public class WishlistTests
{
    private static WishlistAggregate NewWishlist() => WishlistAggregate.Create(Guid.NewGuid()).Value;

    [Fact]
    public void Adding_same_product_twice_does_not_duplicate()
    {
        var wishlist = NewWishlist();
        var product = Guid.NewGuid();

        wishlist.AddItem(product, null, priceAlert: true, stockAlert: false);
        wishlist.AddItem(product, null, priceAlert: false, stockAlert: true);

        wishlist.Items.Should().HaveCount(1);
        var item = wishlist.Items.Single();
        item.PriceAlert.Should().BeFalse();
        item.StockAlert.Should().BeTrue();
    }

    [Fact]
    public void Remove_absent_product_fails()
        => NewWishlist().RemoveItem(Guid.NewGuid()).IsFailure.Should().BeTrue();

    [Fact]
    public void SetAlerts_updates_flags()
    {
        var wishlist = NewWishlist();
        var product = Guid.NewGuid();
        wishlist.AddItem(product, null, false, false);

        wishlist.SetAlerts(product, priceAlert: true, stockAlert: true).IsSuccess.Should().BeTrue();
        wishlist.Items.Single().PriceAlert.Should().BeTrue();
    }
}
