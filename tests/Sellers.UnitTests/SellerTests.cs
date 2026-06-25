using FluentAssertions;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Xunit;

namespace Sellers.UnitTests;

public class SellerTests
{
    private static Seller NewSeller()
        => Seller.Register(Guid.NewGuid(), "Boutique Cotonou", 0.10m).Value;

    [Fact]
    public void Register_demarre_en_Pending_et_KYB_NotStarted()
    {
        var seller = NewSeller();

        seller.Status.Should().Be(SellerStatus.Pending);
        seller.KybStatus.Should().Be(KybStatus.NotStarted);
        seller.SalesCount.Should().Be(0);
    }

    [Fact]
    public void Register_refuse_une_commission_hors_bornes()
    {
        Seller.Register(Guid.NewGuid(), "X", 1.5m).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AddKybDocument_passe_la_verification_en_revue()
    {
        var seller = NewSeller();

        var result = seller.AddKybDocument(KybDocumentType.BusinessRegistry, "https://cdn/doc.pdf");

        result.IsSuccess.Should().BeTrue();
        seller.KybStatus.Should().Be(KybStatus.InReview);
        seller.KybDocuments.Should().ContainSingle();
    }

    [Fact]
    public void Activation_exige_KYB_valide_et_payout()
    {
        var seller = NewSeller();
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/id.pdf");

        // KYB pas encore validé.
        seller.Activate().IsFailure.Should().BeTrue();

        seller.ApproveKyb().IsSuccess.Should().BeTrue();
        seller.KybStatus.Should().Be(KybStatus.Verified);

        // KYB validé mais pas de payout.
        seller.Activate().IsFailure.Should().BeTrue();

        var payout = PayoutAccount.Create(PayoutProvider.MtnMomo, "+2290190000000", "Hector A.").Value;
        seller.SetPayoutAccount(payout);

        seller.Activate().IsSuccess.Should().BeTrue();
        seller.Status.Should().Be(SellerStatus.Active);
    }

    [Fact]
    public void ApproveKyb_sans_document_echoue()
    {
        var seller = NewSeller();

        seller.ApproveKyb().IsFailure.Should().BeTrue();
    }
}
