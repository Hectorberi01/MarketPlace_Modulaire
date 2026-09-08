using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

// ============================================================================
// LA CAGNOTTE DU CLIENT.
//
// Politique retenue : CAGNOTTE PAR DÉFAUT, RETRAIT À LA DEMANDE.
//
// Un remboursement crédite la cagnotte tout de suite — le client a son argent en
// quelques secondes, sans qu'un administrateur ait à verser quoi que ce soit. S'il
// préfère le récupérer sur son Mobile Money, il demande un retrait, qui suit
// EXACTEMENT le même chemin qu'un retrait vendeur : fonds retenus, validation admin,
// payout FedaPay, réconciliation.
//
// C'est le seul choix qui tienne dans un marché où chaque versement sortant coûte des
// frais et un geste humain : rembourser en cagnotte ne coûte ni l'un ni l'autre, et
// seuls les clients qui veulent vraiment sortir l'argent déclenchent un versement.
// ============================================================================

/// <summary>Soldes de la cagnotte d'un client (disponible + engagé).</summary>
public sealed record GetCustomerWalletQuery(Guid CustomerId) : IQuery<CustomerWalletView>;

/// <summary>Relevé des mouvements de la cagnotte d'un client.</summary>
public sealed record ListCustomerWalletTransactionsQuery(Guid CustomerId, int Take = 50)
    : IQuery<IReadOnlyList<WalletTransactionView>>;

/// <summary>Demandes de retrait de cagnotte d'un client.</summary>
public sealed record ListCustomerWithdrawalsQuery(Guid CustomerId) : IQuery<IReadOnlyList<CustomerWithdrawalView>>;

/// <summary>File admin des retraits de cagnotte en attente de validation.</summary>
public sealed record ListPendingCustomerWithdrawalsQuery : IQuery<IReadOnlyList<PendingCustomerWithdrawalView>>;

/// <summary>
/// Le client demande à récupérer tout ou partie de sa cagnotte sur son Mobile Money.
/// Retient les fonds et crée une demande en attente de validation admin — aucun
/// versement n'est déclenché ici.
/// </summary>
public sealed record RequestCustomerWithdrawalCommand(Guid CustomerId, decimal Amount) : ICommand<CustomerWithdrawalView>;

public sealed class RequestCustomerWithdrawalCommandValidator : AbstractValidator<RequestCustomerWithdrawalCommand>
{
    public RequestCustomerWithdrawalCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant à retirer doit être positif.");
    }
}

internal sealed class GetCustomerWalletQueryHandler : IQueryHandler<GetCustomerWalletQuery, CustomerWalletView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;

    public GetCustomerWalletQueryHandler(IWalletRepository wallets, IWithdrawalRepository withdrawals)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
    }

    public async Task<Result<CustomerWalletView>> Handle(GetCustomerWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Customer, query.CustomerId, cancellationToken);

        // Retraits déjà sortis du disponible : demandés (attente admin) ET en cours de
        // versement. Les omettre ferait « disparaître » l'argent de l'écran du client
        // entre sa demande et l'arrivée du virement — le meilleur moyen de recevoir un
        // message affolé pour un système qui fonctionne.
        var pendingWithdrawal = (await _withdrawals.ListByOwnerAsync(
                WalletOwnerType.Customer, query.CustomerId, cancellationToken))
            .Where(w => w.Status is WithdrawalStatus.Requested or WithdrawalStatus.Processing)
            .Sum(w => w.Amount);

        return wallet is null
            ? new CustomerWalletView(query.CustomerId, 0m, 0m, pendingWithdrawal, "XOF")
            : new CustomerWalletView(
                query.CustomerId,
                wallet.Balance(WalletAccount.Available),
                wallet.Balance(WalletAccount.Reserved),
                pendingWithdrawal,
                wallet.Currency);
    }
}

internal sealed class ListCustomerWalletTransactionsQueryHandler
    : IQueryHandler<ListCustomerWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>
{
    private readonly IWalletTransactionRepository _ledger;

    public ListCustomerWalletTransactionsQueryHandler(IWalletTransactionRepository ledger) => _ledger = ledger;

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(
        ListCustomerWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        var items = await _ledger.ListByOwnerAsync(query.CustomerId, query.Take, cancellationToken);
        IReadOnlyList<WalletTransactionView> views = items.Select(WalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListCustomerWithdrawalsQueryHandler
    : IQueryHandler<ListCustomerWithdrawalsQuery, IReadOnlyList<CustomerWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;

    public ListCustomerWithdrawalsQueryHandler(IWithdrawalRepository withdrawals) => _withdrawals = withdrawals;

    public async Task<Result<IReadOnlyList<CustomerWithdrawalView>>> Handle(
        ListCustomerWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByOwnerAsync(
            WalletOwnerType.Customer, query.CustomerId, cancellationToken);
        IReadOnlyList<CustomerWithdrawalView> views = items.Select(WalletMapper.ToCustomerView).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListPendingCustomerWithdrawalsQueryHandler
    : IQueryHandler<ListPendingCustomerWithdrawalsQuery, IReadOnlyList<PendingCustomerWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IIdentityModuleApi _identity;

    public ListPendingCustomerWithdrawalsQueryHandler(IWithdrawalRepository withdrawals, IIdentityModuleApi identity)
    {
        _withdrawals = withdrawals;
        _identity = identity;
    }

    public async Task<Result<IReadOnlyList<PendingCustomerWithdrawalView>>> Handle(
        ListPendingCustomerWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByStatusAsync(
            WithdrawalStatus.Requested, WalletOwnerType.Customer, cancellationToken);

        var views = new List<PendingCustomerWithdrawalView>();
        foreach (var w in items)
        {
            var user = await _identity.GetUserAsync(w.OwnerId, cancellationToken);
            var payout = await _identity.GetDefaultPayoutAccountAsync(w.OwnerId, cancellationToken);

            var name = user is null ? "Client" : $"{user.FirstName} {user.LastName}".Trim();
            views.Add(new PendingCustomerWithdrawalView(
                w.Id.Value,
                w.OwnerId,
                string.IsNullOrWhiteSpace(name) ? "Client" : name,
                w.Amount,
                w.Currency,
                payout?.Provider,
                payout?.AccountNumber,
                w.CreatedAtUtc));
        }

        return Result.Success<IReadOnlyList<PendingCustomerWithdrawalView>>(views);
    }
}

internal sealed class RequestCustomerWithdrawalCommandHandler
    : ICommandHandler<RequestCustomerWithdrawalCommand, CustomerWithdrawalView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WalletMutations _mutations;
    private readonly IIdentityModuleApi _identity;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RequestCustomerWithdrawalCommandHandler(
        IWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        IIdentityModuleApi identity,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _mutations = mutations;
        _identity = identity;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWithdrawalView>> Handle(
        RequestCustomerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Customer, command.CustomerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.not_found", "Aucune cagnotte pour ce compte."));
        }

        // On vérifie le compte de versement AVANT de retenir les fonds : refuser après
        // avoir débité laisserait le client avec un solde amputé pour une demande qui
        // n'aboutira pas.
        var payout = await _identity.GetDefaultPayoutAccountAsync(command.CustomerId, cancellationToken);
        if (payout is null || !WalletPayout.IsMobileMoney(payout.Provider) || string.IsNullOrWhiteSpace(payout.AccountNumber))
        {
            return Result.Failure<CustomerWithdrawalView>(Error.Validation(
                "wallet.no_payout_account",
                "Aucun compte Mobile Money enregistré. Ajoutez-en un dans vos moyens de paiement."));
        }

        var requested = Withdrawal.Create(wallet, command.Amount);
        if (requested.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(requested.Error);
        }

        var withdrawal = requested.Value;

        // La cagnotte n'a PAS le droit de passer sous zéro (voir WalletPolicy) : un
        // solde insuffisant fait échouer le débit, et rien n'est écrit.
        var debit = await _mutations.DebitAsync(
            WalletOwnerType.Customer, command.CustomerId, WalletAccount.Available, command.Amount,
            wallet.Currency, "withdrawal_request", "withdrawal", withdrawal.Id.Value, cancellationToken);

        if (debit.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(debit.Error);
        }

        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToCustomerView(withdrawal);
    }
}
