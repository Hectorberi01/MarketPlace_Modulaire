using FluentAssertions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// L'INVARIANT COMPTABLE DU MODULE :
///
///     pour chaque (portefeuille, compte) : somme des crédits − somme des débits = solde.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CES TESTS EXISTENT
///
/// Cet invariant paraît évident. Il était pourtant FAUX, et sur le cas le plus ordinaire
/// qui soit : la libération d'un gain à la livraison déplaçait le montant du solde à
/// venir vers le disponible dans l'agrégat, mais n'écrivait au grand livre qu'UNE SEULE
/// ligne — le crédit du disponible. Le débit du « à venir » n'était consigné nulle part.
///
/// Le relevé d'un vendeur affichait donc des crédits que rien ne venait jamais solder, et
/// la somme des écritures s'éloignait un peu plus du solde réel à chaque livraison. Rien
/// ne pouvait le révéler : les soldes vivaient sur un agrégat, les écritures dans une
/// table, et aucun code ne comparait les deux.
///
/// C'est exactement ce que vérifie ce fichier, et c'est pour cela qu'il compare
/// systématiquement les deux représentations plutôt que d'inspecter l'une des deux.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class WalletLedgerInvariantTests
{
    private const string Xof = "XOF";

    [Fact]
    public async Task La_liberation_d_un_gain_ecrit_LES_DEUX_ecritures()
    {
        var (mutations, ledger, wallets) = Harness();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditSellerPendingAsync(sellerId, 10_000m, Xof, orderId, default);
        await mutations.ReleaseSellerAsync(sellerId, 10_000m, Xof, orderId, default);

        // C'EST LA LIGNE QUI MANQUAIT : le débit du solde à venir.
        ledger.Lines.Should().ContainSingle(l =>
            l.Account == WalletAccount.Pending && l.Direction == WalletDirection.Debit && l.Amount == 10_000m);

        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Un_parcours_de_vente_complet_laisse_le_grand_livre_d_accord_avec_les_soldes()
    {
        var (mutations, ledger, wallets) = Harness();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        // Vente confirmée : gain vendeur + trois soldes plateforme.
        await mutations.CreditSellerPendingAsync(sellerId, 10_000m, Xof, orderId, default);
        await mutations.CreditPlatformCommissionAsync(1_000m, Xof, orderId, default);
        await mutations.CreditPlatformProviderFeeAsync(500m, Xof, orderId, default);
        await mutations.CreditPlatformShippingAsync(1_500m, Xof, orderId, default);

        // Livraison : le gain devient retirable.
        await mutations.ReleaseSellerAsync(sellerId, 10_000m, Xof, orderId, default);

        // Retour remboursé : contre-passation du vendeur et de la plateforme.
        await mutations.DebitSellerForRefundAsync(sellerId, 10_000m, Xof, returnId, default);
        await mutations.DebitPlatformCommissionAsync(1_000m, Xof, returnId, default);
        await mutations.DebitPlatformProviderFeeAsync(500m, Xof, returnId, default);

        AssertLedgerMatchesBalances(wallets, ledger);

        // Et les comptes retombent bien où ils doivent : le vendeur à zéro, la plateforme
        // n'ayant gardé que les frais de livraison, qui ne se remboursent pas.
        var seller = wallets.Get(WalletOwnerType.Seller, sellerId)!;
        seller.Balance(WalletAccount.Available).Should().Be(0m);

        var platform = wallets.Get(WalletOwnerType.Platform, Wallet.PlatformOwnerId)!;
        platform.Balance(WalletAccount.Commission).Should().Be(0m);
        platform.Balance(WalletAccount.Shipping).Should().Be(1_500m);
    }

    [Fact]
    public async Task Un_debit_refuse_n_ecrit_aucune_ligne()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, 1_000m, Xof,
            "refund", "order", orderId, default);

        var debit = await mutations.DebitAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, 5_000m, Xof,
            "checkout", "order", orderId, default);

        debit.IsFailure.Should().BeTrue();
        ledger.Lines.Should().HaveCount(1, "le crédit initial, et rien d'autre");
        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Un_remboursement_de_retour_credite_la_cagnotte_ET_reprend_le_gain_du_vendeur()
    {
        var (mutations, ledger, wallets) = Harness();
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        // Vente : 10 000 nets au vendeur, 1 000 de commission, 500 de frais.
        await mutations.CreditSellerPendingAsync(sellerId, 10_000m, Xof, orderId, default);
        await mutations.CreditPlatformCommissionAsync(1_000m, Xof, orderId, default);
        await mutations.CreditPlatformProviderFeeAsync(500m, Xof, orderId, default);
        await mutations.ReleaseSellerAsync(sellerId, 10_000m, Xof, orderId, default);

        // Retour : le client est remboursé du BRUT (11 500), le vendeur perd son net,
        // la plateforme restitue commission et frais.
        await mutations.CreditCustomerRefundAsync(buyerId, 11_500m, Xof, returnId, default);
        await mutations.DebitSellerForRefundAsync(sellerId, 10_000m, Xof, returnId, default);
        await mutations.DebitPlatformCommissionAsync(1_000m, Xof, returnId, default);
        await mutations.DebitPlatformProviderFeeAsync(500m, Xof, returnId, default);

        AssertLedgerMatchesBalances(wallets, ledger);

        var customer = wallets.Get(WalletOwnerType.Customer, buyerId)!;
        customer.Balance(WalletAccount.Available).Should().Be(11_500m);

        // Ce que la plateforme a repris finance exactement ce qu'elle a crédité : le
        // remboursement ne coûte rien de plus que la vente n'avait rapporté.
        var seller = wallets.Get(WalletOwnerType.Seller, sellerId)!;
        seller.Balance(WalletAccount.Available).Should().Be(0m);

        var platform = wallets.Get(WalletOwnerType.Platform, Wallet.PlatformOwnerId)!;
        platform.Balance(WalletAccount.Commission).Should().Be(0m);
        platform.Balance(WalletAccount.Provider).Should().Be(0m);
    }

    [Fact]
    public async Task Le_credit_de_cagnotte_et_la_contre_passation_ont_des_verrous_INDEPENDANTS()
    {
        var (mutations, ledger, wallets) = Harness();
        var returnId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(Guid.NewGuid(), 5_000m, Xof, returnId, default);

        // LE PIÈGE QU'ON ÉVITE ICI : si les deux mouvements portaient la même
        // référence, créditer le client marquerait le retour « déjà traité » et le
        // vendeur ne serait JAMAIS débité — la plateforme paierait le remboursement de
        // sa poche, silencieusement.
        (await mutations.CustomerAlreadyRefundedAsync(returnId, default)).Should().BeTrue();
        (await mutations.RefundAlreadyReversedAsync(returnId, default)).Should().BeFalse();

        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Un_client_ne_peut_pas_retirer_plus_que_sa_cagnotte()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(buyerId, 3_000m, Xof, Guid.NewGuid(), default);

        var debit = await mutations.DebitAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, 3_001m, Xof,
            "withdrawal_request", "withdrawal", Guid.NewGuid(), default);

        // La cagnotte est de l'argent que la plateforme DOIT au client. La laisser
        // passer sous zéro ouvrirait un découvert que personne n'a consenti.
        debit.IsFailure.Should().BeTrue();
        wallets.Get(WalletOwnerType.Customer, buyerId)!.Balance(WalletAccount.Available).Should().Be(3_000m);
        AssertLedgerMatchesBalances(wallets, ledger);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DÉPENSE DE LA CAGNOTTE AU CHECKOUT : engager, consommer, ou rendre.
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Engager_puis_consommer_la_cagnotte_vide_le_reserve_et_pas_le_disponible()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(buyerId, 10_000m, Xof, Guid.NewGuid(), default);

        var reserved = await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, WalletAccount.Reserved,
            4_000m, Xof, "checkout_reserve", WalletMutations.CheckoutReserveReferenceType, orderId, default);

        reserved.Should().Be(4_000m);

        var wallet = wallets.Get(WalletOwnerType.Customer, buyerId)!;
        wallet.Balance(WalletAccount.Available).Should().Be(6_000m);
        wallet.Balance(WalletAccount.Reserved).Should().Be(4_000m);

        var spent = await mutations.DebitAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Reserved, 4_000m, Xof,
            "checkout_spend", WalletMutations.CheckoutSpendReferenceType, orderId, default);

        spent.IsSuccess.Should().BeTrue();
        wallet.Balance(WalletAccount.Reserved).Should().Be(0m);
        wallet.Balance(WalletAccount.Available).Should().Be(6_000m, "consommer n'entame pas le disponible");

        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Deux_commandes_simultanees_ne_peuvent_pas_engager_deux_fois_la_meme_cagnotte()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(buyerId, 5_000m, Xof, Guid.NewGuid(), default);

        // C'EST LE SCÉNARIO DES DEUX ONGLETS. Les deux commandes croient disposer de
        // 5 000 : sans le compte réservé, les deux passeraient et l'une des deux serait
        // payée avec de l'argent qui n'existe plus.
        var first = await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, WalletAccount.Reserved,
            5_000m, Xof, "checkout_reserve", WalletMutations.CheckoutReserveReferenceType, Guid.NewGuid(), default);

        var second = await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, WalletAccount.Reserved,
            5_000m, Xof, "checkout_reserve", WalletMutations.CheckoutReserveReferenceType, Guid.NewGuid(), default);

        first.Should().Be(5_000m);
        second.Should().Be(0m, "le disponible est vide : la seconde commande n'engage rien");

        wallets.Get(WalletOwnerType.Customer, buyerId)!.Balance(WalletAccount.Reserved).Should().Be(5_000m);
        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Un_paiement_echoue_rend_la_cagnotte_engagee_au_disponible()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(buyerId, 8_000m, Xof, Guid.NewGuid(), default);
        await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, WalletAccount.Reserved,
            3_000m, Xof, "checkout_reserve", WalletMutations.CheckoutReserveReferenceType, orderId, default);

        await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Reserved, WalletAccount.Available,
            3_000m, Xof, "checkout_release", WalletMutations.CheckoutReleaseReferenceType, orderId, default);

        var wallet = wallets.Get(WalletOwnerType.Customer, buyerId)!;
        wallet.Balance(WalletAccount.Available).Should().Be(8_000m, "le client retrouve exactement sa cagnotte");
        wallet.Balance(WalletAccount.Reserved).Should().Be(0m);

        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Engagement_consommation_et_liberation_ont_des_verrous_INDEPENDANTS()
    {
        var (mutations, ledger, wallets) = Harness();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditCustomerRefundAsync(buyerId, 2_000m, Xof, Guid.NewGuid(), default);
        await mutations.TransferAsync(
            WalletOwnerType.Customer, buyerId, WalletAccount.Available, WalletAccount.Reserved,
            2_000m, Xof, "checkout_reserve", WalletMutations.CheckoutReserveReferenceType, orderId, default);

        // LE PIÈGE : une référence commune ferait que l'engagement marque la commande
        // « déjà traitée », et la consommation n'aurait jamais lieu — le client garderait
        // une cagnotte bloquée pour une commande qu'il a bel et bien reçue.
        (await mutations.HasLedgerEntryAsync(WalletMutations.CheckoutReserveReferenceType, orderId, default))
            .Should().BeTrue();
        (await mutations.HasLedgerEntryAsync(WalletMutations.CheckoutSpendReferenceType, orderId, default))
            .Should().BeFalse();
        (await mutations.HasLedgerEntryAsync(WalletMutations.CheckoutReleaseReferenceType, orderId, default))
            .Should().BeFalse();

        AssertLedgerMatchesBalances(wallets, ledger);
    }

    [Fact]
    public async Task Un_retrait_ne_peut_JAMAIS_depasser_le_solde_disponible()
    {
        var (mutations, _, wallets) = Harness();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Alimenté par un gain de vente libéré à la livraison. Ce test passait par le
        // portefeuille coursier ; la garde testée est la même pour le vendeur, et c'est
        // le vendeur qui retire aujourd'hui.
        await mutations.CreditSellerPendingAsync(sellerId, 5_000m, Xof, orderId, default);
        await mutations.ReleaseSellerAsync(sellerId, 5_000m, Xof, orderId, default);
        var wallet = wallets.Get(WalletOwnerType.Seller, sellerId)!;

        var excessive = Withdrawal.Create(wallet, 500_000m);

        // LE BUG QUE CE TEST FERME. La politique autorise le solde disponible d'un
        // vendeur à passer sous zéro — nécessaire aux CONTRE-PASSATIONS, pour reprendre
        // son gain à quelqu'un déjà payé sur une vente remboursée.
        //
        // Le retrait s'appuyait sur ce même débit : demander 500 000 avec 5 000 au solde
        // passait, et à la validation admin le versement partait pour de bon. On ne
        // reprend pas de l'argent arrivé sur un Mobile Money.
        excessive.IsFailure.Should().BeTrue();
        excessive.Error.Code.Should().Be("wallet.insufficient_funds");

        Withdrawal.Create(wallet, 5_000m).IsSuccess.Should().BeTrue("le solde exact reste retirable");
    }

    [Fact]
    public async Task Le_controle_de_reconciliation_ne_signale_rien_quand_tout_concorde()
    {
        var (mutations, ledger, wallets) = Harness();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await mutations.CreditSellerPendingAsync(sellerId, 10_000m, Xof, orderId, default);
        await mutations.ReleaseSellerAsync(sellerId, 10_000m, Xof, orderId, default);
        await mutations.CreditPlatformCommissionAsync(1_000m, Xof, orderId, default);

        var report = await Reconcile(wallets, ledger);

        report.IsBalanced.Should().BeTrue();
        report.Drifts.Should().BeEmpty();
        report.TotalAbsoluteDrift.Should().Be(0m);
        report.WalletsChecked.Should().Be(2, "le vendeur et la plateforme");
    }

    [Fact]
    public async Task Le_controle_de_reconciliation_voit_un_solde_modifie_sans_ecriture()
    {
        var (mutations, ledger, wallets) = Harness();
        var sellerId = Guid.NewGuid();

        await mutations.CreditSellerPendingAsync(sellerId, 10_000m, Xof, Guid.NewGuid(), default);

        // LA PANNE QU'ON CHERCHE À DÉTECTER : un solde bougé directement sur l'agrégat,
        // sans la ligne de grand livre qui le justifie. C'est exactement la forme qu'avait
        // le bug de la libération des gains.
        wallets.Get(WalletOwnerType.Seller, sellerId)!.Credit(WalletAccount.Available, 750m);

        var report = await Reconcile(wallets, ledger);

        report.IsBalanced.Should().BeFalse();
        report.TotalAbsoluteDrift.Should().Be(750m);
        report.Drifts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                OwnerId = sellerId,
                Account = nameof(WalletAccount.Available),
                WalletBalance = 750m,
                LedgerBalance = 0m,
                Drift = 750m
            });
    }

    [Fact]
    public async Task Le_controle_de_reconciliation_voit_une_ecriture_orpheline()
    {
        var (mutations, ledger, wallets) = Harness();
        await mutations.CreditSellerPendingAsync(Guid.NewGuid(), 10_000m, Xof, Guid.NewGuid(), default);

        // Une écriture pour un propriétaire dont aucun portefeuille n'existe : un parcours
        // des seuls portefeuilles ne la verrait jamais, et l'argent resterait invisible.
        var fantome = Guid.NewGuid();
        await ledger.AddAsync(WalletTransaction.ForOwner(
            WalletOwnerType.Courier, fantome, WalletAccount.Available,
            WalletDirection.Credit, 4_200m, Xof, "orphelin", "test", Guid.NewGuid()));

        var report = await Reconcile(wallets, ledger);

        report.IsBalanced.Should().BeFalse();
        report.Drifts.Should().ContainSingle(d => d.OwnerId == fantome && d.WalletId == Guid.Empty)
            .Which.LedgerBalance.Should().Be(4_200m);
    }

    private static async Task<Marketplace.Modules.Settlement.Contracts.WalletReconciliationView> Reconcile(
        FakeWalletRepository wallets, FakeLedger ledger)
    {
        var handler = new ReconcileWalletsQueryHandler(wallets, ledger);
        var result = await handler.Handle(new ReconcileWalletsQuery(), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>
    /// Le contrôle lui-même : pour chaque compte de chaque portefeuille, la somme signée
    /// des écritures doit rendre le solde. C'est la version en mémoire du contrôle de
    /// réconciliation exposé par <c>SumForAccountAsync</c>.
    /// </summary>
    private static void AssertLedgerMatchesBalances(FakeWalletRepository wallets, FakeLedger ledger)
    {
        foreach (var wallet in wallets.All)
        {
            foreach (var account in wallet.Accounts)
            {
                var sum = ledger.Lines
                    .Where(l => l.OwnerId == wallet.OwnerId
                                && l.OwnerType == wallet.OwnerType
                                && l.Account == account.Account)
                    .Sum(l => l.Direction == WalletDirection.Credit ? l.Amount : -l.Amount);

                sum.Should().Be(account.Balance,
                    $"le grand livre doit rendre le solde « {account.Account} » du portefeuille {wallet.OwnerType}");
            }
        }
    }

    private static (WalletMutations, FakeLedger, FakeWalletRepository) Harness()
    {
        var wallets = new FakeWalletRepository();
        var ledger = new FakeLedger();
        return (new WalletMutations(wallets, ledger), ledger, wallets);
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private readonly List<Wallet> _wallets = new();

        public IReadOnlyList<Wallet> All => _wallets;

        public Wallet? Get(WalletOwnerType ownerType, Guid ownerId)
            => _wallets.FirstOrDefault(w => w.OwnerType == ownerType && w.OwnerId == ownerId);

        public Task<Wallet?> GetAsync(WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Get(ownerType, ownerId));

        public Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_wallets.FirstOrDefault(w => w.Id == id));

        public Task<Wallet?> GetPlatformAsync(CancellationToken cancellationToken = default)
            => GetAsync(WalletOwnerType.Platform, Wallet.PlatformOwnerId, cancellationToken);

        public Task AddAsync(Wallet wallet, CancellationToken cancellationToken = default)
        {
            _wallets.Add(wallet);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Wallet>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Wallet>>(_wallets);
    }

    private sealed class FakeLedger : IWalletTransactionRepository
    {
        public List<WalletTransaction> Lines { get; } = new();

        public Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
        {
            Lines.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(
            WalletOwnerType ownerType, Guid ownerId, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WalletTransaction>>(
                Lines.Where(l => l.OwnerType == ownerType && l.OwnerId == ownerId).Take(take).ToList());

        public Task<IReadOnlyList<WalletTransaction>> ListInPeriodAsync(
            WalletOwnerType ownerType, Guid ownerId, DateTime fromUtc, DateTime toUtc,
            int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WalletTransaction>>(
                Lines.Where(l => l.OwnerType == ownerType
                        && l.OwnerId == ownerId
                        && l.CreatedAtUtc >= fromUtc
                        && l.CreatedAtUtc < toUtc)
                    .OrderByDescending(l => l.CreatedAtUtc)
                    .Take(take)
                    .ToList());

        // Le MÊME filtre que le dépôt réel, borne droite exclue comprise : un double qui
        // arrondit la règle fait passer des tests que la production échoue.
        public Task<LedgerPeriodTotal> SumInPeriodAsync(
            WalletOwnerType ownerType, Guid ownerId, string referenceType,
            WalletDirection direction, DateTime fromUtc, DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            var retenues = Lines.Where(l => l.OwnerType == ownerType
                && l.OwnerId == ownerId
                && l.ReferenceType == referenceType
                && l.Direction == direction
                && l.CreatedAtUtc >= fromUtc
                && l.CreatedAtUtc < toUtc)
                .ToList();

            return Task.FromResult(new LedgerPeriodTotal(retenues.Sum(l => l.Amount), retenues.Count));
        }

        public Task<bool> ExistsForReferenceAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
            => Task.FromResult(Lines.Any(l => l.ReferenceType == referenceType && l.ReferenceId == referenceId));

        public Task<decimal> SumForAccountAsync(
            Guid ownerId, WalletOwnerType ownerType, WalletAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(Lines
                .Where(l => l.OwnerId == ownerId && l.OwnerType == ownerType && l.Account == account)
                .Sum(l => l.Direction == WalletDirection.Credit ? l.Amount : -l.Amount));

        public Task<IReadOnlyList<LedgerAccountSum>> SumAllAccountsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LedgerAccountSum>>(Lines
                .GroupBy(l => new { l.OwnerId, l.OwnerType, l.Account })
                .Select(g => new LedgerAccountSum(
                    g.Key.OwnerId, g.Key.OwnerType, g.Key.Account,
                    g.Sum(l => l.Direction == WalletDirection.Credit ? l.Amount : -l.Amount)))
                .ToList());
    }
}
