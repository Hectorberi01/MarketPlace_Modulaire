using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>Soldes du portefeuille d'un vendeur (à venir + principal).</summary>
public sealed record GetSellerWalletQuery(Guid SellerId) : IQuery<SellerWalletView>;

/// <summary>Historique des mouvements du portefeuille d'un vendeur.</summary>
public sealed record ListSellerWalletTransactionsQuery(Guid SellerId, int Take = 50) : IQuery<IReadOnlyList<WalletTransactionView>>;

/// <summary>Demandes de retrait d'un vendeur.</summary>
public sealed record ListWithdrawalsQuery(Guid SellerId) : IQuery<IReadOnlyList<WithdrawalView>>;

/// <summary>File des demandes de retrait en attente de validation admin (tous vendeurs).</summary>
public sealed record ListPendingWithdrawalsQuery : IQuery<IReadOnlyList<PendingWithdrawalView>>;

/// <summary>
/// Versements demandés au PSP et NON confirmés (tous vendeurs). Sans cette vue, cet
/// état — le seul où le vendeur est débité sans avoir été payé — serait invisible pour
/// l'admin : il ne figure ni dans la file d'attente, ni dans l'historique des payés.
/// </summary>
public sealed record ListProcessingWithdrawalsQuery : IQuery<IReadOnlyList<ProcessingWithdrawalView>>;

/// <summary>Soldes du portefeuille de la plateforme (commission + livraison).</summary>
public sealed record GetPlatformWalletQuery : IQuery<PlatformWalletView>;

/// <summary>Historique des mouvements du portefeuille de la plateforme.</summary>
public sealed record ListPlatformWalletTransactionsQuery(int Take = 50) : IQuery<IReadOnlyList<WalletTransactionView>>;

internal static class WalletMapper
{
    public static SellerWalletView ToView(SellerWallet w, decimal pendingWithdrawal)
        => new(w.SellerId, w.PendingBalance, w.AvailableBalance, pendingWithdrawal, w.Currency);

    public static PlatformWalletView ToView(PlatformWallet w)
        => new(w.CommissionBalance, w.ProviderFeeBalance, w.ShippingBalance, w.RefundsBalance, w.Currency);

    public static CustomerRefundView ToView(CustomerRefund r)
        => new(r.Id.Value, r.OrderId, r.BuyerId, r.Amount, r.Currency, r.Reason, r.Msisdn, r.Provider,
               r.Status.ToString(), r.ProviderRef, PresentableFailure(r.FailureReason), r.CreatedAtUtc, r.CompletedAtUtc);

    public static WalletTransactionView ToView(WalletTransaction t)
        => new(t.Id, t.Account.ToString(), t.Direction.ToString(), t.Amount, t.Currency, t.Reason, t.ReferenceType, t.ReferenceId, t.CreatedAtUtc);

    public static WithdrawalView ToView(Withdrawal w)
        => new(w.Id.Value, w.SellerId, w.Amount, w.Currency, w.Status.ToString(), w.ProviderRef,
               PresentableFailure(w.FailureReason), w.CreatedAtUtc, w.CompletedAtUtc);

    /// <summary>
    /// Filet de sécurité à la LECTURE : un motif d'échec technique ne sort jamais.
    ///
    /// La passerelle FedaPay a longtemps recopié la réponse HTTP brute du PSP dans
    /// <c>FailureReason</c> ; elle ne le fait plus, mais les retraits déjà en base
    /// gardent ce texte, et le vendeur lit encore aujourd'hui
    /// « FedaPay — création refusée (403) : {"message":"Opération non autorisée"…} ».
    /// Réécrire l'historique serait une migration destructive pour un bénéfice nul :
    /// on neutralise donc à l'affichage. Et si une future passerelle refaisait la
    /// même erreur, le vendeur en serait protégé d'office.
    /// </summary>
    private static string? PresentableFailure(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return reason;

        var looksTechnical =
            reason.Contains('{') ||                                   // corps JSON
            reason.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("FedaPay", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Exception", StringComparison.OrdinalIgnoreCase);

        return looksTechnical
            ? "Versement refusé par l'opérateur. Vos fonds ont été recrédités sur votre solde."
            : reason;
    }
}

internal sealed class GetSellerWalletQueryHandler : IQueryHandler<GetSellerWalletQuery, SellerWalletView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;

    public GetSellerWalletQueryHandler(ISellerWalletRepository wallets, IWithdrawalRepository withdrawals)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
    }

    public async Task<Result<SellerWalletView>> Handle(GetSellerWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetBySellerAsync(query.SellerId, cancellationToken);

        // Somme retenue : retraits demandés (attente admin) ET versements en cours chez le
        // PSP. Les deux ont déjà quitté le solde principal. Omettre les seconds ferait
        // disparaître l'argent de l'écran du vendeur pendant tout le versement.
        var pendingWithdrawal = (await _withdrawals.ListBySellerAsync(query.SellerId, cancellationToken))
            .Where(w => w.Status is WithdrawalStatus.Requested or WithdrawalStatus.Processing)
            .Sum(w => w.Amount);

        // Portefeuille pas encore créé (aucune vente confirmée) : soldes à zéro.
        return wallet is null
            ? new SellerWalletView(query.SellerId, 0m, 0m, pendingWithdrawal, "XOF")
            : WalletMapper.ToView(wallet, pendingWithdrawal);
    }
}

internal sealed class ListSellerWalletTransactionsQueryHandler : IQueryHandler<ListSellerWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>
{
    private readonly IWalletTransactionRepository _ledger;

    public ListSellerWalletTransactionsQueryHandler(IWalletTransactionRepository ledger) => _ledger = ledger;

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(ListSellerWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        var items = await _ledger.ListByOwnerAsync(query.SellerId, query.Take, cancellationToken);
        IReadOnlyList<WalletTransactionView> views = items.Select(WalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListWithdrawalsQueryHandler : IQueryHandler<ListWithdrawalsQuery, IReadOnlyList<WithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;

    public ListWithdrawalsQueryHandler(IWithdrawalRepository withdrawals) => _withdrawals = withdrawals;

    public async Task<Result<IReadOnlyList<WithdrawalView>>> Handle(ListWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<WithdrawalView> views = items.Select(WalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListPendingWithdrawalsQueryHandler : IQueryHandler<ListPendingWithdrawalsQuery, IReadOnlyList<PendingWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly ISellerModuleApi _sellers;

    public ListPendingWithdrawalsQueryHandler(IWithdrawalRepository withdrawals, ISellerModuleApi sellers)
    {
        _withdrawals = withdrawals;
        _sellers = sellers;
    }

    public async Task<Result<IReadOnlyList<PendingWithdrawalView>>> Handle(ListPendingWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByStatusAsync(WithdrawalStatus.Requested, cancellationToken);

        var views = new List<PendingWithdrawalView>();
        foreach (var w in items)
        {
            var seller = await _sellers.GetSellerAsync(w.SellerId, cancellationToken);
            views.Add(new PendingWithdrawalView(
                w.Id.Value,
                w.SellerId,
                string.IsNullOrWhiteSpace(seller?.ShopName) ? "Vendeur" : seller!.ShopName,
                w.Amount,
                w.Currency,
                seller?.Payout?.Provider,
                seller?.Payout?.AccountNumber,
                w.CreatedAtUtc));
        }

        return Result.Success<IReadOnlyList<PendingWithdrawalView>>(views);
    }
}

internal sealed class ListProcessingWithdrawalsQueryHandler : IQueryHandler<ListProcessingWithdrawalsQuery, IReadOnlyList<ProcessingWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly ISellerModuleApi _sellers;

    public ListProcessingWithdrawalsQueryHandler(IWithdrawalRepository withdrawals, ISellerModuleApi sellers)
    {
        _withdrawals = withdrawals;
        _sellers = sellers;
    }

    public async Task<Result<IReadOnlyList<ProcessingWithdrawalView>>> Handle(ListProcessingWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByStatusAsync(WithdrawalStatus.Processing, cancellationToken);

        var views = new List<ProcessingWithdrawalView>();
        foreach (var w in items)
        {
            var seller = await _sellers.GetSellerAsync(w.SellerId, cancellationToken);
            views.Add(new ProcessingWithdrawalView(
                w.Id.Value,
                w.SellerId,
                string.IsNullOrWhiteSpace(seller?.ShopName) ? "Vendeur" : seller!.ShopName,
                w.Amount,
                w.Currency,
                w.ProviderRef,
                w.FailureReason, // trace de l'incident éventuel (timeout, démarrage indéterminé…)
                w.CreatedAtUtc,
                w.SentToPspAtUtc));
        }

        // Les plus anciens d'abord : ce sont eux qui traînent, donc ceux à traiter.
        return Result.Success<IReadOnlyList<ProcessingWithdrawalView>>(
            views.OrderBy(v => v.SentToPspAtUtc ?? v.CreatedAtUtc).ToList());
    }
}

internal sealed class GetPlatformWalletQueryHandler : IQueryHandler<GetPlatformWalletQuery, PlatformWalletView>
{
    private readonly IPlatformWalletRepository _wallets;

    public GetPlatformWalletQueryHandler(IPlatformWalletRepository wallets) => _wallets = wallets;

    public async Task<Result<PlatformWalletView>> Handle(GetPlatformWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(cancellationToken);
        return wallet is null
            ? new PlatformWalletView(0m, 0m, 0m, 0m, "XOF")
            : WalletMapper.ToView(wallet);
    }
}

internal sealed class ListPlatformWalletTransactionsQueryHandler : IQueryHandler<ListPlatformWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>
{
    private readonly IWalletTransactionRepository _ledger;

    public ListPlatformWalletTransactionsQueryHandler(IWalletTransactionRepository ledger) => _ledger = ledger;

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(ListPlatformWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        var items = await _ledger.ListByOwnerAsync(PlatformWallet.SingletonId, query.Take, cancellationToken);
        IReadOnlyList<WalletTransactionView> views = items.Select(WalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}
