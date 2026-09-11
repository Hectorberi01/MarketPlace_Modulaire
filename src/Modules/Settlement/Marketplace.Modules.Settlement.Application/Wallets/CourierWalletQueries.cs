using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>Soldes du portefeuille d'un coursier (à venir + retirable).</summary>
public sealed record GetCourierWalletQuery(Guid CourierId) : IQuery<CourierWalletView>;

/// <summary>Relevé des mouvements du portefeuille d'un coursier.</summary>
public sealed record ListCourierWalletTransactionsQuery(Guid CourierId, int Take = 50)
    : IQuery<IReadOnlyList<WalletTransactionView>>;

/// <summary>File admin des retraits livreur en attente de validation.</summary>
public sealed record ListPendingCourierWithdrawalsQuery : IQuery<IReadOnlyList<PendingCourierWithdrawalView>>;

/// <summary>
/// Retraits COURSIERS dont le versement est parti chez le prestataire sans être
/// confirmé.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CETTE FILE N'EXISTAIT PAS, ET LES RETRAITS COURSIERS Y TOMBAIENT QUAND MÊME.
///
/// `ApproveWithdrawalCommandHandler` est indifférent au type de propriétaire : il
/// résout le compte de versement depuis `withdrawal.OwnerType` et traite un coursier
/// exactement comme un vendeur. Un retrait coursier approuvé quittait donc la file
/// d'attente — la seule que le module exposait pour eux — et n'entrait NULLE PART.
/// Ni écran, ni compteur, ni alerte : un coursier débité, son argent en vol, et
/// aucune surface pour s'en apercevoir.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ListProcessingCourierWithdrawalsQuery : IQuery<IReadOnlyList<ProcessingWithdrawalView>>;

/// <summary>Demandes de retrait d'un coursier.</summary>
public sealed record ListCourierWithdrawalsQuery(Guid CourierId) : IQuery<IReadOnlyList<CustomerWithdrawalView>>;

/// <summary>
/// Le coursier demande le versement de son solde sur son Mobile Money.
///
/// Retient les fonds et crée une demande en attente de validation admin — aucun
/// versement n'est déclenché ici. Exactement le chemin du vendeur et du client : un
/// retrait est un retrait, et le lot 1 l'a écrit une seule fois pour cette raison.
/// </summary>
public sealed record RequestCourierWithdrawalCommand(Guid CourierId, decimal Amount) : ICommand<CustomerWithdrawalView>;

public sealed class RequestCourierWithdrawalCommandValidator : AbstractValidator<RequestCourierWithdrawalCommand>
{
    public RequestCourierWithdrawalCommandValidator()
    {
        RuleFor(c => c.CourierId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant à retirer doit être positif.");
    }
}

internal sealed class GetCourierWalletQueryHandler : IQueryHandler<GetCourierWalletQuery, CourierWalletView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;

    public GetCourierWalletQueryHandler(IWalletRepository wallets, IWithdrawalRepository withdrawals)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
    }

    public async Task<Result<CourierWalletView>> Handle(GetCourierWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Courier, query.CourierId, cancellationToken);

        var pendingWithdrawal = (await _withdrawals.ListByOwnerAsync(
                WalletOwnerType.Courier, query.CourierId, cancellationToken))
            .Where(w => w.Status is WithdrawalStatus.Requested or WithdrawalStatus.Processing)
            .Sum(w => w.Amount);

        return wallet is null
            ? new CourierWalletView(query.CourierId, 0m, 0m, pendingWithdrawal, "XOF")
            : new CourierWalletView(
                query.CourierId,
                wallet.Balance(WalletAccount.Pending),
                wallet.Balance(WalletAccount.Available),
                pendingWithdrawal,
                wallet.Currency);
    }
}

internal sealed class ListCourierWalletTransactionsQueryHandler
    : IQueryHandler<ListCourierWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>
{
    private readonly IWalletTransactionRepository _ledger;

    public ListCourierWalletTransactionsQueryHandler(IWalletTransactionRepository ledger) => _ledger = ledger;

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(
        ListCourierWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        var items = await _ledger.ListByOwnerAsync(
            WalletOwnerType.Courier, query.CourierId, query.Take, cancellationToken);
        IReadOnlyList<WalletTransactionView> views = items.Select(WalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListPendingCourierWithdrawalsQueryHandler
    : IQueryHandler<ListPendingCourierWithdrawalsQuery, IReadOnlyList<PendingCourierWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IDeliveryModuleApi _couriers;

    public ListPendingCourierWithdrawalsQueryHandler(
        IWithdrawalRepository withdrawals, IDeliveryModuleApi couriers)
    {
        _withdrawals = withdrawals;
        _couriers = couriers;
    }

    public async Task<Result<IReadOnlyList<PendingCourierWithdrawalView>>> Handle(
        ListPendingCourierWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByStatusAsync(
            WithdrawalStatus.Requested, WalletOwnerType.Courier, cancellationToken);

        var views = new List<PendingCourierWithdrawalView>();
        foreach (var w in items)
        {
            var courier = await _couriers.GetCourierAsync(w.OwnerId, cancellationToken);

            views.Add(new PendingCourierWithdrawalView(
                w.Id.Value,
                w.OwnerId,
                courier?.DisplayName ?? "Coursier",
                // Le statut est affiché SANS bloquer le retrait : un coursier suspendu
                // garde le droit de récupérer ce qu'il a gagné. L'admin doit simplement
                // le voir avant de valider.
                courier?.Status ?? "Inconnu",
                w.Amount,
                w.Currency,
                courier?.Payout?.Provider,
                courier?.Payout?.AccountNumber,
                w.CreatedAtUtc));
        }

        return Result.Success<IReadOnlyList<PendingCourierWithdrawalView>>(views);
    }
}

internal sealed class ListProcessingCourierWithdrawalsQueryHandler
    : IQueryHandler<ListProcessingCourierWithdrawalsQuery, IReadOnlyList<ProcessingWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IDeliveryModuleApi _couriers;

    public ListProcessingCourierWithdrawalsQueryHandler(
        IWithdrawalRepository withdrawals, IDeliveryModuleApi couriers)
    {
        _withdrawals = withdrawals;
        _couriers = couriers;
    }

    public async Task<Result<IReadOnlyList<ProcessingWithdrawalView>>> Handle(
        ListProcessingCourierWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByStatusAsync(
            WithdrawalStatus.Processing, WalletOwnerType.Courier, cancellationToken);

        var views = new List<ProcessingWithdrawalView>();
        foreach (var w in items)
        {
            var courier = await _couriers.GetCourierAsync(w.OwnerId, cancellationToken);

            // On réutilise `ProcessingWithdrawalView` — dont le champ s'appelle
            // « SellerName » — comme le fait déjà la file clients. Le nom est devenu
            // générique le jour où le retrait a cessé d'être réservé aux vendeurs ;
            // l'écran, lui, sait de quelle file il vient.
            views.Add(new ProcessingWithdrawalView(
                w.Id.Value,
                w.OwnerId,
                string.IsNullOrWhiteSpace(courier?.DisplayName) ? "Coursier" : courier!.DisplayName,
                w.Amount,
                w.Currency,
                w.ProviderRef,
                WalletMapper.SansNoteDEtape(w.FailureReason),
                w.CreatedAtUtc,
                w.SentToPspAtUtc));
        }

        // Les plus anciens d'abord : ce sont eux qui traînent, donc ceux à traiter.
        return Result.Success<IReadOnlyList<ProcessingWithdrawalView>>(
            views.OrderBy(v => v.SentToPspAtUtc ?? v.CreatedAtUtc).ToList());
    }
}

internal sealed class ListCourierWithdrawalsQueryHandler
    : IQueryHandler<ListCourierWithdrawalsQuery, IReadOnlyList<CustomerWithdrawalView>>
{
    private readonly IWithdrawalRepository _withdrawals;

    public ListCourierWithdrawalsQueryHandler(IWithdrawalRepository withdrawals) => _withdrawals = withdrawals;

    public async Task<Result<IReadOnlyList<CustomerWithdrawalView>>> Handle(
        ListCourierWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByOwnerAsync(
            WalletOwnerType.Courier, query.CourierId, cancellationToken);

        IReadOnlyList<CustomerWithdrawalView> views = items.Select(WalletMapper.ToCustomerView).ToList();
        return Result.Success(views);
    }
}

internal sealed class RequestCourierWithdrawalCommandHandler
    : ICommandHandler<RequestCourierWithdrawalCommand, CustomerWithdrawalView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WalletMutations _mutations;
    private readonly IDeliveryModuleApi _couriers;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RequestCourierWithdrawalCommandHandler(
        IWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        IDeliveryModuleApi couriers,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _mutations = mutations;
        _couriers = couriers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWithdrawalView>> Handle(
        RequestCourierWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Courier, command.CourierId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.not_found", "Aucun portefeuille pour ce coursier."));
        }

        // ON VÉRIFIE LE COMPTE DE VERSEMENT, PAS LE STATUT DU COURSIER.
        //
        // Un coursier suspendu garde le droit de récupérer ce qu'il a déjà gagné :
        // suspendre interdit de nouvelles courses, cela ne confisque pas les anciennes.
        // Le résolveur de versement applique la même règle, et les deux doivent rester
        // d'accord — sans quoi une demande acceptée ici échouerait à la validation admin,
        // en laissant les fonds retenus entre-temps.
        var courier = await _couriers.GetCourierAsync(command.CourierId, cancellationToken);
        if (courier?.Payout is null || string.IsNullOrWhiteSpace(courier.Payout.AccountNumber))
        {
            return Result.Failure<CustomerWithdrawalView>(Error.Validation(
                "wallet.no_payout_account", "Aucun compte Mobile Money enregistré pour ce coursier."));
        }

        var requested = Withdrawal.Create(wallet, command.Amount);
        if (requested.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(requested.Error);
        }

        var withdrawal = requested.Value;

        var debit = await _mutations.DebitAsync(
            WalletOwnerType.Courier, command.CourierId, WalletAccount.Available, command.Amount,
            wallet.Currency, "withdrawal_request", WalletMutations.WithdrawalReferenceType, withdrawal.Id.Value, cancellationToken);

        if (debit.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(debit.Error);
        }

        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToCustomerView(withdrawal);
    }
}
