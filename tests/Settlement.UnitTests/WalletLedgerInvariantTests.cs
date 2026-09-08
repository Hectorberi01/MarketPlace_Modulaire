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
    }

    private sealed class FakeLedger : IWalletTransactionRepository
    {
        public List<WalletTransaction> Lines { get; } = new();

        public Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
        {
            Lines.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WalletTransaction>>(
                Lines.Where(l => l.OwnerId == ownerId).Take(take).ToList());

        public Task<bool> ExistsForReferenceAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
            => Task.FromResult(Lines.Any(l => l.ReferenceType == referenceType && l.ReferenceId == referenceId));

        public Task<decimal> SumForAccountAsync(
            Guid ownerId, WalletOwnerType ownerType, WalletAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(Lines
                .Where(l => l.OwnerId == ownerId && l.OwnerType == ownerType && l.Account == account)
                .Sum(l => l.Direction == WalletDirection.Credit ? l.Amount : -l.Amount));
    }
}
