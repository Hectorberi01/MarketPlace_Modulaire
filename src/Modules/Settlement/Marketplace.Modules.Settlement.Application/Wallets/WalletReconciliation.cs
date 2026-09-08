using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Confronte chaque solde de portefeuille à la somme des écritures du grand livre.
///
/// Deux requêtes agrégées, quel que soit le nombre de portefeuilles : la réponse est
/// assez rapide pour être appelée depuis un écran admin, et assez peu coûteuse pour
/// être branchée sur une sonde périodique le jour où on en voudra une.
/// </summary>
public sealed record ReconcileWalletsQuery : IQuery<WalletReconciliationView>;

internal sealed class ReconcileWalletsQueryHandler : IQueryHandler<ReconcileWalletsQuery, WalletReconciliationView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWalletTransactionRepository _ledger;

    public ReconcileWalletsQueryHandler(IWalletRepository wallets, IWalletTransactionRepository ledger)
    {
        _wallets = wallets;
        _ledger = ledger;
    }

    public async Task<Result<WalletReconciliationView>> Handle(
        ReconcileWalletsQuery query, CancellationToken cancellationToken)
    {
        var wallets = await _wallets.ListAllAsync(cancellationToken);
        var ledgerSums = await _ledger.SumAllAccountsAsync(cancellationToken);

        var byKey = ledgerSums.ToDictionary(
            s => (s.OwnerType, s.OwnerId, s.Account),
            s => s.Sum);

        var drifts = new List<WalletDriftView>();
        var accountsChecked = 0;

        foreach (var wallet in wallets)
        {
            foreach (var balance in wallet.Accounts)
            {
                accountsChecked++;

                var key = (wallet.OwnerType, wallet.OwnerId, balance.Account);
                byKey.TryGetValue(key, out var ledger);

                // Marqué comme vu : ce qui restera dans le dictionnaire à la fin, ce sont
                // les écritures ORPHELINES — un grand livre qui mouvemente un compte
                // qu'aucun portefeuille ne porte. C'est le pire des cas, et le seul qu'un
                // simple parcours des portefeuilles ne verrait jamais.
                byKey.Remove(key);

                if (balance.Balance == ledger)
                {
                    continue;
                }

                drifts.Add(new WalletDriftView(
                    wallet.Id.Value, wallet.OwnerType.ToString(), wallet.OwnerId,
                    balance.Account.ToString(), balance.Balance, ledger,
                    balance.Balance - ledger));
            }
        }

        // Les orphelins : des écritures sans solde en face. Signalés avec un solde de
        // portefeuille à zéro et un identifiant de portefeuille vide — il n'y en a pas.
        foreach (var ((ownerType, ownerId, account), sum) in byKey)
        {
            if (sum == 0m)
            {
                continue;
            }

            accountsChecked++;
            drifts.Add(new WalletDriftView(
                Guid.Empty, ownerType.ToString(), ownerId, account.ToString(),
                0m, sum, -sum));
        }

        var view = new WalletReconciliationView(
            wallets.Count,
            accountsChecked,
            drifts.Sum(d => Math.Abs(d.Drift)),
            drifts.OrderByDescending(d => Math.Abs(d.Drift)).ToList(),
            DateTime.UtcNow);

        return Result.Success(view);
    }
}
