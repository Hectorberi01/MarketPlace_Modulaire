using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Offers.Domain.Offers;
using Xunit;

namespace Offers.UnitTests;

public class OfferTests
{
    private static Offer NewOffer()
    {
        var sku = Sku.Create("SKU-001").Value;
        var price = Money.Create(7000m, "XOF").Value;
        return Offer.Create(Guid.NewGuid(), Guid.NewGuid(), sku, price, OfferCondition.New, FulfillmentType.Fbs, Guid.NewGuid(), 2).Value;
    }

    [Fact]
    public void Create_demarre_active()
    {
        var offer = NewOffer();
        offer.Status.Should().Be(OfferStatus.Active);
        offer.BasePrice.Amount.Should().Be(7000m);
        offer.BasePrice.Currency.Should().Be("XOF");
    }

    [Fact]
    public void ChangePrice_met_a_jour_le_prix()
    {
        var offer = NewOffer();
        offer.ChangePrice(Money.Create(5100m, "XOF").Value);
        offer.BasePrice.Amount.Should().Be(5100m);
    }

    [Fact]
    public void Transitions_de_statut()
    {
        var offer = NewOffer();
        offer.Pause();
        offer.Status.Should().Be(OfferStatus.Paused);
        offer.MarkOutOfStock();
        offer.Status.Should().Be(OfferStatus.OutOfStock);
        offer.Activate();
        offer.Status.Should().Be(OfferStatus.Active);
    }

    [Fact]
    public void Money_refuse_un_montant_negatif()
    {
        Money.Create(-1m, "XOF").IsFailure.Should().BeTrue();
    }
}
