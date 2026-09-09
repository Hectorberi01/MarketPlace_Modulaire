using FluentAssertions;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// UN REMBOURSEMENT REPART PAR OÙ IL EST VENU.
///
/// Deux garanties opposées, et il faut les deux.
///
/// La PREMIÈRE : il ne sort jamais en espèces plus que ce que la commande a fait
/// entrer en caisse. Sinon un administrateur convertit, d'un clic, une dette de
/// cagnotte en sortie de trésorerie majorée des frais du prestataire.
///
/// La SECONDE : le montant demandé est TOUJOURS intégralement réparti. Refuser
/// l'excédent aurait rendu non remboursable toute commande réglée en cagnotte, faute
/// de route qui crédite une cagnotte hors du flux « retour ». C'est la moitié des
/// tests ci-dessous, et c'est celle qu'on oublie d'écrire.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RepartitionDuRemboursementTests
{
    [Fact]
    public void Une_commande_payee_entierement_en_especes_se_rembourse_entierement_en_especes()
    {
        var split = RefundSplit.Compute(amount: 13_000m, cashCollected: 13_000m, cashAlreadyRefunded: 0m);

        split.CashAmount.Should().Be(13_000m);
        split.WalletAmount.Should().Be(0m);
        split.RequiresPayout.Should().BeTrue();
    }

    [Fact]
    public void L_exemple_de_l_audit_huit_mille_en_especes_cinq_mille_en_cagnotte()
    {
        // 13 000 dus, dont 8 000 seulement sont entrés en caisse.
        var split = RefundSplit.Compute(amount: 13_000m, cashCollected: 8_000m, cashAlreadyRefunded: 0m);

        split.CashAmount.Should().Be(8_000m);
        split.WalletAmount.Should().Be(5_000m);
    }

    [Fact]
    public void Une_commande_payee_entierement_en_cagnotte_ne_declenche_aucun_versement()
    {
        var split = RefundSplit.Compute(amount: 13_000m, cashCollected: 0m, cashAlreadyRefunded: 0m);

        split.CashAmount.Should().Be(0m);
        split.WalletAmount.Should().Be(13_000m);

        // C'est ce booléen qui évite d'exiger un numéro Mobile Money dont aucune
        // écriture n'aura besoin.
        split.RequiresPayout.Should().BeFalse();
    }

    [Theory]
    [InlineData(13_000, 8_000, 0)]
    [InlineData(5_000, 8_000, 0)]
    [InlineData(13_000, 8_000, 8_000)]
    [InlineData(1, 0, 0)]
    [InlineData(9_999.99, 3_333.33, 1_111.11)]
    public void Les_deux_parts_redonnent_toujours_le_montant_demande(
        decimal montant, decimal encaisse, decimal dejaRendu)
    {
        var split = RefundSplit.Compute(montant, encaisse, dejaRendu);

        // Aucune tolérance : la part cagnotte est un reste, pas un second calcul.
        split.Total.Should().Be(montant);
    }

    [Fact]
    public void Un_second_remboursement_ne_peut_plus_puiser_dans_des_especes_deja_rendues()
    {
        // 8 000 encaissés, 8 000 déjà rendus en espèces : il ne reste plus de caisse.
        var split = RefundSplit.Compute(amount: 5_000m, cashCollected: 8_000m, cashAlreadyRefunded: 8_000m);

        split.CashAmount.Should().Be(0m);
        split.WalletAmount.Should().Be(5_000m);
    }

    [Fact]
    public void Un_remboursement_partiel_reste_entierement_en_especes_tant_que_la_caisse_suffit()
    {
        var split = RefundSplit.Compute(amount: 3_000m, cashCollected: 8_000m, cashAlreadyRefunded: 0m);

        split.CashAmount.Should().Be(3_000m);
        split.WalletAmount.Should().Be(0m);
    }

    [Fact]
    public void Une_caisse_deja_depassee_ne_produit_jamais_une_part_especes_negative()
    {
        // Donnée abîmée ou antérieure au plafond : plus rendu en espèces qu'encaissé.
        // Sans la borne à zéro, la part cagnotte dépasserait le montant demandé.
        var split = RefundSplit.Compute(amount: 2_000m, cashCollected: 8_000m, cashAlreadyRefunded: 12_000m);

        split.CashAmount.Should().Be(0m);
        split.WalletAmount.Should().Be(2_000m);
        split.Total.Should().Be(2_000m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Un_montant_nul_ou_negatif_ne_repartit_rien(decimal montant)
    {
        var split = RefundSplit.Compute(montant, cashCollected: 8_000m, cashAlreadyRefunded: 0m);

        split.CashAmount.Should().Be(0m);
        split.WalletAmount.Should().Be(0m);
        split.RequiresPayout.Should().BeFalse();
    }

    // ── Les deux jambes sont deux lignes ────────────────────────────────────────

    [Fact]
    public void La_jambe_cagnotte_nait_terminee_et_echappe_a_la_reconciliation()
    {
        var jambe = CustomerRefund.CreateWallet(
            Guid.NewGuid(), Guid.NewGuid(), 5_000m, "XOF", "Geste commercial");

        // « Processing » l'enverrait dans la file de réconciliation, qui interrogerait
        // le prestataire sur un versement qui n'a jamais été demandé.
        jambe.Status.Should().Be(CustomerRefundStatus.Completed);
        jambe.IsProcessing.Should().BeFalse();
        jambe.Channel.Should().Be(CustomerRefundChannel.Wallet);
        jambe.CompletedAtUtc.Should().NotBeNull();
        jambe.SentToPspAtUtc.Should().BeNull();
    }

    [Fact]
    public void La_jambe_cagnotte_n_exige_ni_numero_ni_operateur()
    {
        var jambe = CustomerRefund.CreateWallet(
            Guid.NewGuid(), Guid.NewGuid(), 5_000m, "XOF", "Geste commercial");

        // Les exiger obligerait à inventer une donnée qu'aucune écriture n'utilisera.
        jambe.Msisdn.Should().BeEmpty();
        jambe.Provider.Should().BeEmpty();
    }

    [Fact]
    public void La_jambe_especes_reste_en_attente_du_prestataire()
    {
        var jambe = CustomerRefund.CreateCash(
            Guid.NewGuid(), Guid.NewGuid(), 8_000m, "XOF", "Geste commercial", "+2290190000000", "mtnmomo");

        jambe.Status.Should().Be(CustomerRefundStatus.Processing);
        jambe.Channel.Should().Be(CustomerRefundChannel.Cash);
        jambe.SentToPspAtUtc.Should().NotBeNull();
        jambe.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Deux_remboursements_successifs_ne_depassent_pas_la_valeur_de_la_commande()
    {
        // Le scénario que la colonne « canal » ferme. Commande de 13 000, dont 8 000
        // encaissés. Premier remboursement intégral : 8 000 en espèces + 5 000 en
        // cagnotte, DEUX lignes en base.
        const decimal valeurCommande = 13_000m;
        const decimal encaisse = 8_000m;

        var premier = RefundSplit.Compute(13_000m, encaisse, cashAlreadyRefunded: 0m);
        premier.CashAmount.Should().Be(8_000m);
        premier.WalletAmount.Should().Be(5_000m);

        // Le plafond de VALEUR somme les deux canaux. Si la jambe cagnotte n'était pas
        // persistée, il ne verrait que 8 000 et autoriserait 5 000 de plus — soit
        // 18 000 rendus sur une commande à 13 000, chaque contrôle passant proprement.
        var totalRendu = premier.CashAmount + premier.WalletAmount;
        (valeurCommande - totalRendu).Should().Be(0m);
    }
}
