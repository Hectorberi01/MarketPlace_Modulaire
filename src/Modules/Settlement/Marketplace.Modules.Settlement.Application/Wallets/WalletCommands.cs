using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

// ============================================================================
// Nouveau flux de retrait :
//   1. Le vendeur DEMANDE un retrait → les fonds sont retenus (débités du solde
//      principal) et la demande est créée à l'état « Requested ». Aucun payout.
//   2. L'ADMIN valide (ApproveWithdrawal) → payout FedaPay réel → Completed, ou
//      refuse (RejectWithdrawal) → les fonds sont recrédités.
// ============================================================================

/// <summary>
/// Demande de retrait d'un vendeur : retient les fonds (débit du solde principal)
/// et crée une demande en attente de validation admin. NE déclenche PAS de payout.
/// </summary>
public sealed record RequestWithdrawalCommand(Guid SellerId, decimal Amount) : ICommand<WithdrawalView>;

internal sealed class RequestWithdrawalCommandHandler : ICommandHandler<RequestWithdrawalCommand, WithdrawalView>
{
    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WalletMutations _mutations;
    private readonly ISellerModuleApi _sellers;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RequestWithdrawalCommandHandler(
        IWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        ISellerModuleApi sellers,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _mutations = mutations;
        _sellers = sellers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(RequestWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Seller, command.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        // Un compte Mobile Money est requis pour qu'une demande ait un sens :
        // on valide AVANT de retenir les fonds (aucune écriture inutile sinon).
        var seller = await _sellers.GetSellerAsync(command.SellerId, cancellationToken);
        var account = seller?.Payout;
        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.no_payout_account", "Aucun compte de versement Mobile Money configuré."));
        }

        // Le retrait est créé AVANT le débit : son identifiant est la référence portée par
        // l'écriture de grand livre, et c'est lui qui rend la ligne explicable.
        var withdrawal = Withdrawal.Create(wallet, command.Amount);

        // Retenue des fonds. Le solde et son écriture partent ensemble — c'est la seule
        // API qui les lie. Un solde insuffisant remonte l'erreur du domaine telle quelle.
        var debit = await _mutations.DebitAsync(
            WalletOwnerType.Seller, command.SellerId, WalletAccount.Available, command.Amount,
            wallet.Currency, "withdrawal_request", "withdrawal", withdrawal.Id.Value, cancellationToken);

        if (debit.IsFailure)
        {
            return Result.Failure<WithdrawalView>(debit.Error);
        }

        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }
}

/// <summary>
/// Validation admin d'une demande de retrait : déclenche le payout FedaPay Mobile
/// Money. Succès → Completed ; échec → Failed + recrédit des fonds.
/// </summary>
public sealed record ApproveWithdrawalCommand(Guid WithdrawalId) : ICommand<WithdrawalView>;

internal sealed class ApproveWithdrawalCommandHandler : ICommandHandler<ApproveWithdrawalCommand, WithdrawalView>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WalletMutations _mutations;
    private readonly ISellerModuleApi _sellers;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ApproveWithdrawalCommandHandler(
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        ISellerModuleApi sellers,
        IPayoutModuleApi payouts,
        ISettlementUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _mutations = mutations;
        _sellers = sellers;
        _payouts = payouts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(ApproveWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(new WithdrawalId(command.WithdrawalId), cancellationToken);
        if (withdrawal is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.withdrawal_not_found", "Demande de retrait introuvable."));
        }

        if (!withdrawal.IsPendingApproval)
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.withdrawal_not_pending", "Cette demande a déjà été traitée."));
        }

        var seller = await _sellers.GetSellerAsync(withdrawal.OwnerId, cancellationToken);
        var account = seller?.Payout;
        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return await FailAndRefundAsync(withdrawal, "Aucun compte de versement Mobile Money configuré.", cancellationToken);
        }

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: withdrawal.Amount,
                Currency: withdrawal.Currency,
                BeneficiaryName: string.IsNullOrWhiteSpace(account.AccountName) ? seller!.ShopName : account.AccountName,
                Msisdn: account.AccountNumber,
                // L'opérateur du vendeur détermine à lui seul le routage PSP (mode ET
                // pays). On ne passe plus de code pays « en dur » : c'était le bug qui
                // expédiait tous les numéros vers le Bénin.
                Provider: account.Provider,
                Reference: $"withdrawal:{withdrawal.Id}"),
            cancellationToken);

        switch (outcome.Status)
        {
            // Rejet DÉFINITIF du PSP : rien n'est parti → on peut recréditer sans risque.
            case PayoutOutcomeStatus.Failed:
                return await FailAndRefundAsync(withdrawal, outcome.Error ?? "Échec du versement.", cancellationToken);

            // Issue INDÉTERMINÉE (timeout, 5xx…) : le versement est peut-être parti.
            // On NE REMBOURSE PAS — sinon l'admin pourrait re-valider et déclencher un
            // SECOND versement. Le retrait passe « en cours » ; la réconciliation
            // interrogera FedaPay et tranchera (Completed ou Failed + remboursement).
            case PayoutOutcomeStatus.Unknown:
                withdrawal.MarkProcessing(outcome.ProviderReference, outcome.Error);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);

            // Accepté = créé + démarré chez FedaPay. Ce n'est QUE « started » : l'argent
            // n'est pas encore arrivé. On ne clôture donc PAS en Completed ici — c'est la
            // réconciliation, sur le statut « sent », qui le fera.
            default:
                withdrawal.MarkProcessing(outcome.ProviderReference);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);
        }
    }

    // Échec du payout après retenue des fonds : marque Failed et recrédite le solde.
    private async Task<Result<WithdrawalView>> FailAndRefundAsync(
        Withdrawal withdrawal, string reason, CancellationToken ct)
    {
        withdrawal.Fail(reason);
        await _mutations.CreditAsync(
            withdrawal.OwnerType, withdrawal.OwnerId, WalletAccount.Available, withdrawal.Amount,
            withdrawal.Currency, "withdrawal_refund", "withdrawal", withdrawal.Id.Value, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<WithdrawalView>(Error.Failure("wallet.withdrawal_failed", reason));
    }
}

/// <summary>Refus admin d'une demande de retrait : recrédite les fonds retenus.</summary>
public sealed record RejectWithdrawalCommand(Guid WithdrawalId, string Reason) : ICommand<WithdrawalView>;

internal sealed class RejectWithdrawalCommandHandler : ICommandHandler<RejectWithdrawalCommand, WithdrawalView>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WalletMutations _mutations;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RejectWithdrawalCommandHandler(
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        ISettlementUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _mutations = mutations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(RejectWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(new WithdrawalId(command.WithdrawalId), cancellationToken);
        if (withdrawal is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.withdrawal_not_found", "Demande de retrait introuvable."));
        }

        if (!withdrawal.IsPendingApproval)
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.withdrawal_not_pending", "Cette demande a déjà été traitée."));
        }

        await _mutations.CreditAsync(
            withdrawal.OwnerType, withdrawal.OwnerId, WalletAccount.Available, withdrawal.Amount,
            withdrawal.Currency, "withdrawal_reject", "withdrawal", withdrawal.Id.Value, cancellationToken);

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Demande refusée par l'administrateur." : command.Reason.Trim();
        withdrawal.Reject(reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }
}

/// <summary>Petit utilitaire partagé pour identifier un compte de versement Mobile Money.</summary>
internal static class WalletPayout
{
    /// <summary>
    /// Opérateurs que l'on sait RÉELLEMENT reverser via FedaPay (mode + pays connus).
    /// Doit rester aligné sur <c>FedaPayPayoutGateway.ResolveRoute</c>.
    ///
    /// « wave » et « fedapay » ont été RETIRÉS volontairement : « fedapay » n'est pas
    /// un opérateur, et « wave » est ambigu (FedaPay distingue wave_ci et wave_sn — sans
    /// pays sur le compte du vendeur, impossible de trancher). Les accepter revenait à
    /// router leurs numéros vers MTN Bénin. Mieux vaut refuser la demande de retrait tout
    /// de suite que de débiter le vendeur pour échouer — ou pire, mal payer — ensuite.
    ///
    /// Pour les supporter : ajouter un pays au compte de versement du vendeur, puis
    /// étendre la table de routage du gateway.
    /// </summary>
    public static bool IsMobileMoney(string provider)
        => provider.ToLowerInvariant() is "mtnmomo" or "moovmoney" or "celtis";
}
