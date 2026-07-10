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
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISellerModuleApi _sellers;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RequestWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        ISellerModuleApi sellers,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _sellers = sellers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(RequestWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetBySellerAsync(command.SellerId, cancellationToken);
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

        // Débite le solde principal (échoue si montant invalide / solde insuffisant).
        var debit = wallet.Withdraw(command.Amount);
        if (debit.IsFailure)
        {
            return Result.Failure<WithdrawalView>(debit.Error);
        }

        var currency = wallet.Currency;
        var withdrawal = Withdrawal.Create(command.SellerId, command.Amount, currency);
        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            command.SellerId, WalletAccount.Available, WalletDirection.Debit, command.Amount, currency,
            "withdrawal_request", "withdrawal", withdrawal.Id.Value), cancellationToken);

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
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISellerModuleApi _sellers;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ApproveWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        ISellerModuleApi sellers,
        IPayoutModuleApi payouts,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
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

        var wallet = await _wallets.GetBySellerAsync(withdrawal.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        var seller = await _sellers.GetSellerAsync(withdrawal.SellerId, cancellationToken);
        var account = seller?.Payout;
        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return await FailAndRefundAsync(withdrawal, wallet, "Aucun compte de versement Mobile Money configuré.", cancellationToken);
        }

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: withdrawal.Amount,
                Currency: withdrawal.Currency,
                BeneficiaryName: string.IsNullOrWhiteSpace(account.AccountName) ? seller!.ShopName : account.AccountName,
                Msisdn: account.AccountNumber,
                CountryCode: "BJ",
                // Opérateur Mobile Money du vendeur (MtnMomo/MoovMoney/Celtis…) : le
                // gateway FedaPay le traduit en « mode » (mtn_open/moov/sbin). Tous
                // les vendeurs n'utilisent pas MTN — on ne hardcode donc plus le mode.
                Provider: account.Provider,
                Reference: $"withdrawal:{withdrawal.Id}"),
            cancellationToken);

        if (!outcome.Success)
        {
            return await FailAndRefundAsync(withdrawal, wallet, outcome.Error ?? "Échec du versement.", cancellationToken);
        }

        withdrawal.Complete(outcome.ProviderReference);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }

    // Échec du payout après retenue des fonds : marque Failed et recrédite le solde.
    private async Task<Result<WithdrawalView>> FailAndRefundAsync(
        Withdrawal withdrawal, SellerWallet wallet, string reason, CancellationToken ct)
    {
        withdrawal.Fail(reason);
        wallet.CreditAvailable(withdrawal.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            withdrawal.SellerId, WalletAccount.Available, WalletDirection.Credit, withdrawal.Amount, withdrawal.Currency,
            "withdrawal_refund", "withdrawal", withdrawal.Id.Value), ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<WithdrawalView>(Error.Failure("wallet.withdrawal_failed", reason));
    }
}

/// <summary>Refus admin d'une demande de retrait : recrédite les fonds retenus.</summary>
public sealed record RejectWithdrawalCommand(Guid WithdrawalId, string Reason) : ICommand<WithdrawalView>;

internal sealed class RejectWithdrawalCommandHandler : ICommandHandler<RejectWithdrawalCommand, WithdrawalView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RejectWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        ISettlementUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
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

        var wallet = await _wallets.GetBySellerAsync(withdrawal.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        wallet.CreditAvailable(withdrawal.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            withdrawal.SellerId, WalletAccount.Available, WalletDirection.Credit, withdrawal.Amount, withdrawal.Currency,
            "withdrawal_reject", "withdrawal", withdrawal.Id.Value), cancellationToken);

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Demande refusée par l'administrateur." : command.Reason.Trim();
        withdrawal.Reject(reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }
}

/// <summary>Petit utilitaire partagé pour identifier un compte de versement Mobile Money.</summary>
internal static class WalletPayout
{
    public static bool IsMobileMoney(string provider)
        => provider.ToLowerInvariant() is "mtnmomo" or "moovmoney" or "wave" or "celtis" or "fedapay";
}
