using FluentAssertions;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;
using Xunit;

namespace Sellers.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// NON-RÉGRESSION §7.3 ET §7.4 — LE DOSSIER DE CONFORMITÉ.
///
/// §7.3 : un KYB validé survivait à la suppression de TOUTES ses pièces. Téléverser,
/// faire valider, supprimer : boutique « Verified » avec zéro justificatif en base,
/// activable et payable, vidée par son propre titulaire, sans trace.
///
/// §7.4 : le rejet ne prenait aucune raison et ne levait aucun événement. Le vendeur
/// découvrait « Rejected » sur son écran de compte, sans motif — et re-téléversait la
/// pièce refusée.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class KybDossierTests
{
    private static Seller NewSeller()
        => Seller.Register(Guid.NewGuid(), "Boutique Cotonou", 0.10m).Value;

    // ─────────────────────────────────────────────────────────────────────────────
    // §7.3
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_piece_validee_ne_peut_plus_etre_supprimee()
    {
        var seller = NewSeller();
        var doc = seller.AddKybDocument(KybDocumentType.BusinessRegistry, "https://cdn/doc.pdf").Value;
        seller.ApproveKyb();

        var result = seller.RemoveKybDocument(doc.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.kyb.document_verified");
        seller.KybDocuments.Should().ContainSingle();
        seller.KybStatus.Should().Be(KybStatus.Verified);
    }

    [Fact]
    public void Une_piece_non_encore_validee_reste_supprimable()
    {
        // Le cas légitime : le vendeur s'est trompé de fichier et le corrige.
        var seller = NewSeller();
        var doc = seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/mauvais.pdf").Value;

        var result = seller.RemoveKybDocument(doc.Id);

        result.IsSuccess.Should().BeTrue();
        seller.KybDocuments.Should().BeEmpty();
    }

    [Fact]
    public void Un_dossier_valide_ne_peut_pas_etre_vide()
    {
        var seller = NewSeller();
        var a = seller.AddKybDocument(KybDocumentType.BusinessRegistry, "https://cdn/a.pdf").Value;
        var b = seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/b.pdf").Value;
        seller.ApproveKyb();

        seller.RemoveKybDocument(a.Id).IsFailure.Should().BeTrue();
        seller.RemoveKybDocument(b.Id).IsFailure.Should().BeTrue();
        seller.KybDocuments.Should().HaveCount(2);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // §7.4
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Le_rejet_exige_un_motif()
    {
        var seller = NewSeller();
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/id.pdf");

        var result = seller.RejectKyb("   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.kyb.reason_required");
        seller.KybStatus.Should().Be(KybStatus.InReview); // le statut n'a pas bougé
    }

    [Fact]
    public void Le_rejet_conserve_le_motif_et_l_annonce()
    {
        var seller = NewSeller();
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/id.pdf");

        seller.RejectKyb("La pièce d'identité est illisible.").IsSuccess.Should().BeTrue();

        seller.KybStatus.Should().Be(KybStatus.Rejected);
        seller.KybRejectionReason.Should().Be("La pièce d'identité est illisible.");
        seller.DomainEvents.OfType<SellerKybRejectedDomainEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be("La pièce d'identité est illisible.");
    }

    [Fact]
    public void Une_nouvelle_piece_efface_le_motif_et_relance_la_revue()
    {
        var seller = NewSeller();
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/flou.pdf");
        seller.RejectKyb("La pièce d'identité est illisible.");

        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/net.pdf");

        seller.KybStatus.Should().Be(KybStatus.InReview);
        // Le motif décrivait le refus précédent : l'afficher pendant la nouvelle revue
        // ferait croire à un second refus.
        seller.KybRejectionReason.Should().BeNull();
    }

    [Fact]
    public void La_validation_efface_le_motif_du_refus_precedent()
    {
        var seller = NewSeller();
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/flou.pdf");
        seller.RejectKyb("La pièce d'identité est illisible.");
        seller.AddKybDocument(KybDocumentType.IdCard, "https://cdn/net.pdf");

        seller.ApproveKyb().IsSuccess.Should().BeTrue();

        seller.KybStatus.Should().Be(KybStatus.Verified);
        seller.KybRejectionReason.Should().BeNull();
    }
}
