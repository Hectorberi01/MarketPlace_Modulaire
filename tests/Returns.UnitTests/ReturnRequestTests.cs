using FluentAssertions;
using Marketplace.Modules.Returns.Domain.Returns;
using Xunit;

namespace Returns.UnitTests;

/// <summary>
/// Le remboursement se déroule en DEUX temps, et ces tests existent pour empêcher
/// qu'on les confonde à nouveau.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE QUE CES TESTS AFFIRMAIENT AVANT, ET QUI ÉTAIT FAUX
///
/// L'ancien test s'appelait « Happy_path_approve_receive_refund » et vérifiait
/// qu'après <c>Refund(5100)</c>, le retour était « Remboursé ». Il passait. Il a
/// toujours passé.
///
/// Et pourtant AUCUN ARGENT NE PARTAIT : rien n'appelait la banque. Le test ne
/// validait pas un remboursement, il validait un changement de variable. Il donnait
/// une confiance parfaitement infondée — le pire service qu'un test puisse rendre.
///
/// Les tests ci-dessous vérifient désormais ce qui compte vraiment : qu'on ne PEUT
/// PAS déclarer un client remboursé sans preuve que l'argent est parti.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class ReturnRequestTests
{
    /// <summary>Ligne à 10 000 XOF : c'est le plafond remboursable de ces tests.</summary>
    private const decimal LineTotal = 10_000m;

    private static ReturnRequest NewRequest(decimal refundable = LineTotal)
        => ReturnRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ReturnReason.Defective, "XOF", refundable).Value;

    /// <summary>Amène un retour jusqu'à l'état « réceptionné », seul état d'où l'on peut rembourser.</summary>
    private static ReturnRequest Received()
    {
        var request = NewRequest();
        request.Approve();
        request.AddReturnTracking("DHL", "RET-1");
        request.MarkReceived();
        return request;
    }

    [Fact]
    public void Created_request_is_requested()
        => NewRequest().Status.Should().Be(ReturnStatus.Requested);

    [Fact]
    public void Parcours_complet_de_la_demande_au_versement()
    {
        var request = Received();

        // ÉTAPE 1 — le vendeur valide. L'argent n'est PAS parti.
        request.ApproveRefund(5100m).IsSuccess.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.RefundPending);
        request.RefundAmount.Should().Be(5100m);
        request.RefundApprovedAtUtc.Should().NotBeNull();
        request.RefundReference.Should().BeNull("aucun versement n'a encore eu lieu");

        // ÉTAPE 2 — l'admin a versé chez FedaPay et fournit la référence.
        request.ConfirmRefundExecuted("FP-REF-42", DateTime.UtcNow).IsSuccess.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Refunded);
        request.RefundReference.Should().Be("FP-REF-42");
        request.RefundExecutedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// LE TEST DE NON-RÉGRESSION LE PLUS IMPORTANT DU FICHIER.
    ///
    /// Valider un remboursement ne doit JAMAIS suffire à le déclarer effectué. C'était
    /// exactement le bug : la base disait « remboursé », le client n'avait rien reçu.
    /// </summary>
    [Fact]
    public void Valider_un_remboursement_ne_le_declare_PAS_effectue()
    {
        var request = Received();

        request.ApproveRefund(5100m);

        request.Status.Should().NotBe(ReturnStatus.Refunded,
            "tant que l'argent n'est pas parti, on ne peut pas prétendre avoir remboursé");
        request.Status.Should().Be(ReturnStatus.RefundPending);
    }

    /// <summary>
    /// La référence de versement est la PREUVE que l'argent est parti. Sans elle, le
    /// statut « Remboursé » ne vaut rien — et l'on retomberait dans le mensonge d'hier.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Confirmer_un_versement_sans_reference_est_refuse(string reference)
    {
        var request = Received();
        request.ApproveRefund(5100m);

        request.ConfirmRefundExecuted(reference, DateTime.UtcNow).IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.RefundPending, "le retour reste en attente de versement");
    }

    /// <summary>
    /// On ne peut pas confirmer un versement qui n'a jamais été décidé. Sans ce garde-fou,
    /// un appel direct sauterait l'étape de validation — et contre-passerait le vendeur
    /// pour un remboursement que personne n'a approuvé.
    /// </summary>
    [Fact]
    public void Confirmer_un_versement_non_valide_est_refuse()
    {
        var request = Received();

        request.ConfirmRefundExecuted("FP-REF-42", DateTime.UtcNow).IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Received);
    }

    /// <summary>Deux confirmations = deux contre-passations. L'idempotence protège le vendeur.</summary>
    [Fact]
    public void Confirmer_deux_fois_le_meme_versement_est_refuse()
    {
        var request = Received();
        request.ApproveRefund(5100m);
        request.ConfirmRefundExecuted("FP-REF-42", DateTime.UtcNow);

        request.ConfirmRefundExecuted("FP-REF-99", DateTime.UtcNow).IsFailure.Should().BeTrue(
            "un second passage débiterait le vendeur une seconde fois pour la même vente");
        request.RefundReference.Should().Be("FP-REF-42", "la première référence fait foi");
    }

    [Fact]
    public void Rembourser_avant_reception_est_refuse()
    {
        var request = NewRequest();
        request.Approve();

        request.ApproveRefund(100m).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_montant_non_positif_est_refuse(decimal amount)
    {
        var request = Received();

        request.ApproveRefund(amount).IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Received);
    }

    [Fact]
    public void Rejected_request_cannot_be_approved()
    {
        var request = NewRequest();
        request.Reject("hors délai");

        request.Approve().IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Rejected);
    }

    /// <summary>
    /// Le plafond est une règle métier, pas un confort d'interface : rien ne doit
    /// permettre de rembourser plus que ce que l'acheteur a payé pour cette ligne.
    ///
    /// Sans cette borne, un zéro de trop dans l'application vendeur engageait un
    /// versement supérieur à la commande — argent avancé par la plateforme, à
    /// récupérer ensuite auprès du vendeur.
    /// </summary>
    [Fact]
    public void Rembourser_plus_que_la_ligne_est_refuse()
    {
        var request = Received();

        request.ApproveRefund(LineTotal + 1m).IsFailure.Should().BeTrue();
        request.Status.Should().Be(ReturnStatus.Received, "aucune décision ne doit avoir été enregistrée");
        request.RefundAmount.Should().BeNull();
    }

    [Fact]
    public void Rembourser_exactement_la_ligne_est_accepte()
    {
        var request = Received();

        request.ApproveRefund(LineTotal).IsSuccess.Should().BeTrue("le remboursement intégral est le cas normal");
        request.RefundAmount.Should().Be(LineTotal);
    }

    /// <summary>
    /// Les retours créés AVANT l'introduction du plafond portent 0. Les borner à 0
    /// rendrait tout remboursement impossible sur le stock existant — on traite donc
    /// cette valeur comme « plafond inconnu ».
    /// </summary>
    [Fact]
    public void Un_plafond_inconnu_n_empeche_pas_le_remboursement()
    {
        var request = NewRequest(refundable: 0m);
        request.Approve();
        request.AddReturnTracking("DHL", "RET-1");
        request.MarkReceived();

        request.ApproveRefund(999_999m).IsSuccess.Should().BeTrue();
    }
}
