using FluentAssertions;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// Le portefeuille générique : quels comptes appartiennent à qui, et qui a le droit de
/// passer sous zéro.
/// </summary>
public class WalletTests
{
    private const string Xof = "XOF";

    private static Wallet Seller() => Wallet.Create(WalletOwnerType.Seller, Guid.NewGuid(), Xof);
    private static Wallet Customer() => Wallet.Create(WalletOwnerType.Customer, Guid.NewGuid(), Xof);
    private static Wallet Courier() => Wallet.Create(WalletOwnerType.Courier, Guid.NewGuid(), Xof);

    [Fact]
    public void Chaque_acteur_recoit_ses_comptes_a_la_creation()
    {
        Seller().Accounts.Select(a => a.Account)
            .Should().BeEquivalentTo(new[] { WalletAccount.Pending, WalletAccount.Available });

        Customer().Accounts.Select(a => a.Account)
            .Should().BeEquivalentTo(new[] { WalletAccount.Available, WalletAccount.Reserved });

        Wallet.CreatePlatform(Xof).Accounts.Select(a => a.Account)
            .Should().BeEquivalentTo(new[]
            {
                WalletAccount.Commission, WalletAccount.Provider,
                WalletAccount.Shipping, WalletAccount.Refunds
            });
    }

    /// <summary>
    /// Un compte qui n'appartient pas à l'acteur est refusé, pas créé en douce. Sans ce
    /// contrôle, une faute de frappe dans un handler créerait un solde fantôme qu'aucun
    /// écran ne montrerait et qu'aucun retrait ne pourrait sortir.
    /// </summary>
    [Fact]
    public void Un_compte_etranger_a_l_acteur_est_refuse()
    {
        var customer = Customer();

        customer.Credit(WalletAccount.Commission, 1_000m).IsFailure.Should().BeTrue();
        customer.Balance(WalletAccount.Commission).Should().Be(0m);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LA RÈGLE DU NÉGATIF
    //
    // Un solde peut passer sous zéro quand il représente une dette de L'ACTEUR envers la
    // plateforme ; jamais quand il représente une dette de la plateforme envers l'acteur.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LE CAS QUI COÛTE DE L'ARGENT : le vendeur a déjà été payé, et l'article revient.
    ///
    /// Refuser le débit faute de solde — le réflexe naturel — laisserait la perte à la
    /// charge de la plateforme : on aurait remboursé le client ET payé le vendeur pour
    /// une vente annulée.
    /// </summary>
    [Fact]
    public void Le_solde_vendeur_peut_devenir_negatif_apres_une_contre_passation()
    {
        var wallet = Seller();
        wallet.Credit(WalletAccount.Pending, 500m);
        wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, 500m);

        var (fromPending, fromAvailable) = wallet.DebitCascade(
            WalletAccount.Pending, WalletAccount.Available, 2_000m);

        fromPending.Should().Be(0m, "le solde à venir est vide, tout est déjà libéré");
        fromAvailable.Should().Be(2_000m);
        wallet.Balance(WalletAccount.Available).Should().Be(-1_500m, "la dette est réelle et doit être visible");
    }

    /// <summary>
    /// Le client, lui, ne peut pas devoir de l'argent à la marketplace : sa cagnotte est
    /// de l'argent qu'on lui doit. Un découvert n'aurait aucun sens comptable.
    /// </summary>
    [Fact]
    public void La_cagnotte_du_client_ne_passe_jamais_sous_zero()
    {
        var wallet = Customer();
        wallet.Credit(WalletAccount.Available, 1_000m);

        var debit = wallet.Debit(WalletAccount.Available, 1_500m);

        debit.IsFailure.Should().BeTrue();
        debit.Error.Code.Should().Be("wallet.insufficient_funds");
        wallet.Balance(WalletAccount.Available).Should().Be(1_000m, "un débit refusé ne laisse aucune trace");
    }

    /// <summary>
    /// Même en cascade : la contre-passation d'un client ne prend que ce qui existe.
    /// </summary>
    [Fact]
    public void La_cascade_respecte_l_interdiction_de_negatif()
    {
        var wallet = Customer();
        wallet.Credit(WalletAccount.Available, 300m);

        var (fromReserved, fromAvailable) = wallet.DebitCascade(
            WalletAccount.Reserved, WalletAccount.Available, 1_000m);

        fromReserved.Should().Be(0m);
        fromAvailable.Should().Be(300m, "on prend ce qu'il y a, pas plus");
        wallet.Balance(WalletAccount.Available).Should().Be(0m);
    }

    [Fact]
    public void Le_livreur_suit_les_memes_regles_que_le_vendeur()
    {
        var wallet = Courier();
        wallet.Credit(WalletAccount.Pending, 2_000m);
        wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, 2_000m);

        wallet.DebitCascade(WalletAccount.Pending, WalletAccount.Available, 3_000m);

        wallet.Balance(WalletAccount.Available).Should().Be(-1_000m);
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Le transfert est PLAFONNÉ au solde source, et c'est délibéré : la libération d'un
    /// gain à la livraison peut être rejouée, ou arriver après une contre-passation qui a
    /// déjà vidé le solde à venir. Transférer plus que ce qui existe créerait de l'argent.
    /// </summary>
    [Fact]
    public void Un_transfert_ne_deplace_jamais_plus_que_le_solde_source()
    {
        var wallet = Seller();
        wallet.Credit(WalletAccount.Pending, 400m);

        var moved = wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, 1_000m);

        moved.Should().Be(400m);
        wallet.Balance(WalletAccount.Pending).Should().Be(0m);
        wallet.Balance(WalletAccount.Available).Should().Be(400m);
    }

    /// <summary>Un montant nul ou négatif ne fait rien, et n'est pas une erreur.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Un_montant_non_positif_est_sans_effet(decimal amount)
    {
        var wallet = Seller();

        wallet.Credit(WalletAccount.Pending, amount).IsSuccess.Should().BeTrue();
        wallet.Debit(WalletAccount.Pending, amount).IsSuccess.Should().BeTrue();
        wallet.Transfer(WalletAccount.Pending, WalletAccount.Available, amount).Should().Be(0m);
        wallet.Balance(WalletAccount.Pending).Should().Be(0m);
    }
}
