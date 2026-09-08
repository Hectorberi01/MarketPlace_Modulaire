using FluentAssertions;
using Xunit;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Cart.UnitTests;

public class CartAggregateTests
{
    private static CartAggregate NewCart() => CartAggregate.Create(Guid.NewGuid(), "XOF").Value;

    private static (Guid offer, Guid product, Guid category, Guid seller, Guid location) Ids()
        => (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Adding_same_offer_twice_merges_quantities()
    {
        var cart = NewCart();
        var (offer, product, category, seller, location) = Ids();

        cart.AddItem(offer, product, category, seller, "SKU-1", location, 7000m, "XOF", 1, 0.10m, 0.05m);
        cart.AddItem(offer, product, category, seller, "SKU-1", location, 7000m, "XOF", 2, 0.10m, 0.05m);

        cart.Items.Should().HaveCount(1);
        cart.Items.Single().Quantity.Should().Be(3);
    }

    [Fact]
    public void Adding_item_in_other_currency_is_rejected()
    {
        var cart = NewCart();
        var (offer, product, category, seller, location) = Ids();

        var result = cart.AddItem(offer, product, category, seller, "SKU-1", location, 50m, "EUR", 1, 0.10m, 0.05m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cart.currency_mismatch");
        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void Updating_quantity_to_zero_removes_the_line()
    {
        var cart = NewCart();
        var (offer, product, category, seller, location) = Ids();
        cart.AddItem(offer, product, category, seller, "SKU-1", location, 7000m, "XOF", 2, 0.10m, 0.05m);

        cart.UpdateItemQuantity(offer, 0);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void Checkout_empty_cart_fails()
    {
        var cart = NewCart();

        cart.MarkCheckedOut().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Checkout_marks_cart_and_raises_event()
    {
        var cart = NewCart();
        var (offer, product, category, seller, location) = Ids();
        cart.AddItem(offer, product, category, seller, "SKU-1", location, 7000m, "XOF", 1, 0.10m, 0.05m);

        var result = cart.MarkCheckedOut();

        result.IsSuccess.Should().BeTrue();
        cart.Status.ToString().Should().Be("CheckedOut");
    }
}
