using FluentAssertions;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;
using Xunit;

namespace Sellers.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// NON-RÉGRESSION §1.2 ET §1.3 — LA SUSPENSION SORT DU MODULE, ET NE SE BLANCHIT PLUS.
///
/// `Suspend()` écrivait un statut et rendait la main : aucun événement, donc aucune
/// dépublication, aucune notification, aucun effet visible par l'acheteur. Et le
/// vendeur suspendu pouvait effacer la sanction lui-même en fermant sa boutique puis
/// en demandant sa réouverture — chaque étape passait sa propre garde.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class SuspensionTests
{
    private static Seller NewActiveSeller()
    {
        var seller = Seller.Register(Guid.NewGuid(), "Boutique Cotonou", 0.10m).Value;
        seller.AddKybDocument(KybDocumentType.BusinessRegistry, "https://cdn/doc.pdf");
        seller.ApproveKyb();
        seller.SetPayoutAccount(PayoutAccount.Create(PayoutProvider.MtnMomo, "+2290190000000", "Hector A.").Value);
        seller.Activate().IsSuccess.Should().BeTrue();
        return seller;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // §1.2 — la suspension doit être annoncée
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Suspend_leve_un_evenement_portant_le_motif()
    {
        var seller = NewActiveSeller();

        seller.Suspend("Contrefaçons signalées sur trois fiches").IsSuccess.Should().BeTrue();

        seller.Status.Should().Be(SellerStatus.Suspended);
        seller.IsSuspended.Should().BeTrue();
        seller.SuspendedOnUtc.Should().NotBeNull();
        seller.SuspensionReason.Should().Be("Contrefaçons signalées sur trois fiches");
        seller.SuspensionCount.Should().Be(1);

        seller.DomainEvents.OfType<SellerSuspendedDomainEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be("Contrefaçons signalées sur trois fiches");
    }

    [Fact]
    public void Suspend_deux_fois_est_refuse()
    {
        var seller = NewActiveSeller();
        seller.Suspend("motif").IsSuccess.Should().BeTrue();

        var result = seller.Suspend("motif");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.seller.already_suspended");
        seller.SuspensionCount.Should().Be(1); // le compteur ne bouge pas sur un refus
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // §1.3 — le blanchiment de suspension
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_vendeur_suspendu_ne_peut_pas_fermer_sa_boutique()
    {
        // C'est le premier maillon de l'enchaînement : suspendre -> fermer ->
        // demander la réactivation -> se faire réactiver, sanction effacée.
        var seller = NewActiveSeller();
        seller.Suspend("motif");

        var result = seller.RequestClosure();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.seller.suspended");
        seller.Status.Should().Be(SellerStatus.Suspended);
    }

    [Fact]
    public void Un_vendeur_suspendu_ne_peut_pas_etre_reactive_par_la_voie_du_compte_ferme()
    {
        var seller = NewActiveSeller();
        seller.Suspend("motif");

        var result = seller.ApproveReactivation();

        result.IsFailure.Should().BeTrue();
        seller.Status.Should().Be(SellerStatus.Suspended);
    }

    [Fact]
    public void Activate_ne_sert_pas_de_levee_deguisee()
    {
        var seller = NewActiveSeller();
        seller.Suspend("motif");

        var result = seller.Activate();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.seller.suspended");
        seller.Status.Should().Be(SellerStatus.Suspended);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // La levée
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LiftSuspension_reactive_mais_garde_la_memoire_de_la_sanction()
    {
        var seller = NewActiveSeller();
        seller.Suspend("motif de la sanction");

        seller.LiftSuspension().IsSuccess.Should().BeTrue();

        seller.Status.Should().Be(SellerStatus.Active);
        seller.SuspendedOnUtc.Should().BeNull();
        seller.SuspensionReason.Should().BeNull();
        // Ce qui reste : le fait que la sanction a eu lieu.
        seller.SuspensionCount.Should().Be(1);
        seller.DomainEvents.OfType<SellerSuspensionLiftedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void LiftSuspension_sur_un_vendeur_non_suspendu_est_refusee()
    {
        var seller = NewActiveSeller();

        var result = seller.LiftSuspension();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sellers.seller.not_suspended");
    }

    [Fact]
    public void Le_compteur_de_suspensions_n_est_jamais_remis_a_zero()
    {
        var seller = NewActiveSeller();

        seller.Suspend("première");
        seller.LiftSuspension();
        seller.Suspend("deuxième");

        seller.SuspensionCount.Should().Be(2);
        seller.SuspensionReason.Should().Be("deuxième");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Le droit de vendre, déjà posé par SellerRights, doit rester cohérent
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_suspendu_ne_vend_pas_et_ne_retire_pas_de_fonds()
    {
        var statut = nameof(SellerStatus.Suspended);

        Marketplace.Modules.Sellers.Contracts.SellerRights.CanSell(statut).Should().BeFalse();
        Marketplace.Modules.Sellers.Contracts.SellerRights.CanWithdraw(statut).Should().BeFalse();
    }
}
