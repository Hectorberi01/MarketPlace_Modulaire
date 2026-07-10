using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Offers.Domain.Offers;
using Xunit;

namespace Offers.UnitTests;

public class OfferTests
{
    private const decimal CommRate = 0.10m;
    private const decimal ProvRate = 0.05m;

    private static Offer NewOffer()
    {
        var sku = Sku.Create("SKU-001").Value;
        // Prix VENDEUR net = 7000 -> prix produit = 7000 + 700 (10%) + 350 (5%) = 8050.
        return Offer.Create(Guid.NewGuid(), Guid.NewGuid(), sku, 7000m, CommRate, ProvRate, "XOF",
            OfferCondition.New, FulfillmentType.Fbs, Guid.NewGuid(), 2).Value;
    }

    [Fact]
    public void Create_demarre_active_et_empile_la_majoration()
    {
        var offer = NewOffer();
        offer.Status.Should().Be(OfferStatus.Active);
        offer.SellerPrice.Should().Be(7000m);
        offer.CommissionAmount.Should().Be(700m);
        offer.ProviderFeeAmount.Should().Be(350m);
        offer.BasePrice.Amount.Should().Be(8050m);
        offer.BasePrice.Currency.Should().Be("XOF");
    }

    [Fact]
    public void ChangePrice_recalcule_le_prix_produit()
    {
        var offer = NewOffer();
        // Nouveau prix VENDEUR = 5000 -> produit = 5000 + 500 + 250 = 5750.
        offer.ChangePrice(5000m, CommRate, ProvRate);
        offer.SellerPrice.Should().Be(5000m);
        offer.CommissionAmount.Should().Be(500m);
        offer.ProviderFeeAmount.Should().Be(250m);
        offer.BasePrice.Amount.Should().Be(5750m);
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
