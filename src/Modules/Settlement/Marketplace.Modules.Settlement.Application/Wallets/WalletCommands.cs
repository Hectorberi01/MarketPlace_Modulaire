using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Demande de retrait d'un vendeur depuis son solde principal. Exécutée
/// immédiatement via un payout FedaPay Mobile Money vers son compte de versement.
/// </summary>
public sealed record RequestWithdrawalCommand(Guid SellerId, decimal Amount) : ICommand<WithdrawalView>;

internal sealed class RequestWithdrawalCommandHandler : ICommandHandler<RequestWithdrawalCommand, WithdrawalView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISellerModuleApi _sellers;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RequestWithdrawalCommandHandler(
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

    public async Task<Result<WithdrawalView>> Handle(RequestWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetBySellerAsync(command.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
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
            "withdrawal", "withdrawal", withdrawal.Id.Value), cancellationToken);

        // Compte de versement Mobile Money du vendeur.
        var seller = await _sellers.GetSellerAsync(command.SellerId, cancellationToken);
        var account = seller?.Payout;
        if (account is null || !IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return await FailAndRefundAsync(withdrawal, wallet, command, currency,
                "Aucun compte de versement Mobile Money configuré.", cancellationToken);
        }

        // Payout FedaPay immédiat.
        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: command.Amount,
                Currency: currency,
                BeneficiaryName: string.IsNullOrWhiteSpace(account.AccountName) ? seller!.ShopName : account.AccountName,
                Msisdn: account.AccountNumber,
                CountryCode: "BJ",
                Reference: $"withdrawal:{withdrawal.Id}"),
            cancellationToken);

        if (!outcome.Success)
        {
            return await FailAndRefundAsync(withdrawal, wallet, command, currency,
                outcome.Error ?? "Échec du versement.", cancellationToken);
        }

        withdrawal.Complete(outcome.ProviderReference);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }

    private async Task<Result<WithdrawalView>> FailAndRefundAsync(
        Withdrawal withdrawal, SellerWallet wallet, RequestWithdrawalCommand command, string currency, string reason, CancellationToken ct)
    {
        // Annulation : on recrédite le solde principal (pas de fonds perdus).
        withdrawal.Fail(reason);
        wallet.CreditAvailable(command.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            command.SellerId, WalletAccount.Available, WalletDirection.Credit, command.Amount, currency,
            "withdrawal_refund", "withdrawal", withdrawal.Id.Value), ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<WithdrawalView>(Error.Failure("wallet.withdrawal_failed", reason));
    }

    private static bool IsMobileMoney(string provider)
        => provider.ToLowerInvariant() is "mtnmomo" or "moovmoney" or "wave";
}
