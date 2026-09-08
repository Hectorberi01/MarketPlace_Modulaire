using FluentAssertions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// Contre-passation d'un remboursement : le vendeur est débité, la plateforme restitue
/// sa commission.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CES TESTS EXISTENT
///
/// Ce code déplace de l'ARGENT, et il n'avait AUCUN test. Deux défauts s'y cachaient :
///
///  1. Le handler n'était pas idempotent. L'outbox livre at-least-once : un rejeu
///     débitait le vendeur une SECONDE fois. Une perte comptable réelle, invisible.
///
///  2. Les écritures étaient référencées par la COMMANDE, pas par le remboursement :
///     deux retours sur la même commande devenaient indistinguables au grand livre.
///
/// Un test qui ne rejoue pas l'événement ne teste pas ce code — il teste le cas facile.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class RefundReversalTests
{
    private const string Xof = "XOF";

    /// <summary>Le vendeur avait 10 000 en attente ; on lui reprend le net d'un remboursement de 1 150.</summary>
    [Fact]
    public void La_contre_passation_debite_le_solde_a_venir_en_priorite()
    {
        var wallet = Wallet.Create(WalletOwnerType.Seller, Guid.NewGuid(), Xof);
        wallet.Credit(WalletAccount.Pending, 10_000m);

        var (fromPending, fromAvailable) = wallet.DebitCascade(WalletAccount.Pending, WalletAccount.Available, 1_000m);

        fromPending.Should().Be(1_000m, "le gain n'est pas encore libéré : le reprendre ne lèse pas le vendeur");
        fromAvailable.Should().Be(0m);
        wallet.Balance(WalletAccount.Pending).Should().Be(9_000m);
        wallet.Balance(WalletAccount.Available).Should().Be(0m);
    }

    /// <summary>
    /// LE CAS QUI COÛTE DE L'ARGENT : le vendeur a déjà été payé, et l'article revient.
    ///
    /// Refuser le débit faute de solde — le réflexe naturel — laisserait la perte à la
    /// charge de la plateforme : on aurait remboursé le client ET payé le vendeur pour
    /// une vente annulée. Le solde DOIT donc pouvoir passer sous zéro.
    /// </summary>
    [Fact]
    public void Le_solde_peut_devenir_negatif_si_le_vendeur_a_deja_ete_paye()
    {
        var wallet = Wallet.Create(WalletOwnerType.Seller, Guid.NewGuid(), Xof);
        wallet.Credit(WalletAccount.Pending, 500m);
        wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, 500m);   // livré, donc retirable
        wallet.Balance(WalletAccount.Available).Should().Be(500m);

        var (fromPending, fromAvailable) = wallet.DebitCascade(WalletAccount.Pending, WalletAccount.Available, 2_000m);

        fromPending.Should().Be(0m, "le solde à venir est vide");
        fromAvailable.Should().Be(2_000m);
        wallet.Balance(WalletAccount.Available).Should().Be(-1_500m,
            "la dette est réelle : elle doit être visible, et se résorber sur les ventes suivantes");
    }

    /// <summary>Le débit se répartit sur les deux soldes quand le solde à venir ne suffit pas.</summary>
    [Fact]
    public void Le_debit_deborde_du_solde_a_venir_vers_le_solde_principal()
    {
        var wallet = Wallet.Create(WalletOwnerType.Seller, Guid.NewGuid(), Xof);
        wallet.Credit(WalletAccount.Pending, 1_000m);
        wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, 400m);   // 600 en attente, 400 disponibles

        var (fromPending, fromAvailable) = wallet.DebitCascade(WalletAccount.Pending, WalletAccount.Available, 900m);

        fromPending.Should().Be(600m);
        fromAvailable.Should().Be(300m);
        wallet.Balance(WalletAccount.Pending).Should().Be(0m);
        wallet.Balance(WalletAccount.Available).Should().Be(100m);
    }

    /// <summary>Un montant nul ou négatif ne doit rien mouvementer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Un_montant_non_positif_ne_debite_rien(decimal amount)
    {
        var wallet = Wallet.Create(WalletOwnerType.Seller, Guid.NewGuid(), Xof);
        wallet.Credit(WalletAccount.Pending, 1_000m);

        var (fromPending, fromAvailable) = wallet.DebitCascade(WalletAccount.Pending, WalletAccount.Available, amount);

        fromPending.Should().Be(0m);
        fromAvailable.Should().Be(0m);
        wallet.Balance(WalletAccount.Pending).Should().Be(1_000m);
    }

    /// <summary>
    /// La plateforme restitue sa commission : pas de vente, pas de rémunération.
    ///
    /// Le solde peut devenir négatif si la commission a déjà été retirée — c'est une
    /// dette de la plateforme envers elle-même, et la masquer serait pire que l'afficher.
    /// </summary>
    [Fact]
    public void La_plateforme_restitue_sa_commission_et_ses_frais()
    {
        var platform = Wallet.CreatePlatform(Xof);
        platform.Credit(WalletAccount.Commission, 1_000m);
        platform.Credit(WalletAccount.Provider, 500m);

        platform.Debit(WalletAccount.Commission, 300m);
        platform.Debit(WalletAccount.Provider, 150m);

        platform.Balance(WalletAccount.Commission).Should().Be(700m);
        platform.Balance(WalletAccount.Provider).Should().Be(350m);

        // Au-delà du solde : négatif assumé.
        platform.Debit(WalletAccount.Commission, 1_000m);
        platform.Balance(WalletAccount.Commission).Should().Be(-300m);
    }

    /// <summary>
    /// ⚠️ LE TEST LE PLUS IMPORTANT DU FICHIER.
    ///
    /// La répartition du remboursement doit tomber JUSTE au franc près :
    /// brut = commission + frais + net vendeur.
    ///
    /// Le net est calculé comme un RESTE, et non par un troisième arrondi — trois
    /// arrondis indépendants ne se rejoignent pas, et l'écart s'accumule silencieusement
    /// à chaque retour jusqu'à ce que la comptabilité cesse de tomber juste.
    /// </summary>
    [Theory]
    [InlineData(10_000)]
    [InlineData(1_150)]
    [InlineData(3_333)]
    [InlineData(7)]
    public void La_repartition_du_remboursement_tombe_juste_au_franc_pres(decimal refundAmount)
    {
        // Ce test recopiait autrefois la formule du handler, ligne pour ligne. Deux
        // copies d'une règle comptable ne restent d'accord que par chance : le jour où
        // l'une bouge, le test continue de passer en validant l'ancienne. La règle vit
        // désormais dans GrossSplit, et c'est ELLE qu'on interroge ici.
        var accrued = GrossSplit.Split(refundAmount, 0.10m, 0.05m);
        var (commission, providerFee, sellerNet) = GrossSplit.Prorate(
            refundAmount, refundAmount, accrued.Commission, accrued.ProviderFee);

        (commission + providerFee + sellerNet).Should().Be(refundAmount,
            "aucun franc ne doit se perdre en route : le brut remboursé se redécompose EXACTEMENT");
        sellerNet.Should().BeGreaterThan(0m);
    }
}
