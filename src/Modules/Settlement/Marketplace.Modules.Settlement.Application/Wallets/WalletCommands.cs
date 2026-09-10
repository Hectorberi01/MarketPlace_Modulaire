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
        var requested = Withdrawal.Create(wallet, command.Amount);
        if (requested.IsFailure)
        {
            return Result.Failure<WithdrawalView>(requested.Error);
        }

        var withdrawal = requested.Value;

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
    private readonly WithdrawalPayoutResolver _payoutAccounts;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ApproveWithdrawalCommandHandler(
        IWithdrawalRepository withdrawals,
        WalletMutations mutations,
        WithdrawalPayoutResolver payoutAccounts,
        IPayoutModuleApi payouts,
        ISettlementUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _mutations = mutations;
        _payoutAccounts = payoutAccounts;
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

        // OÙ VERSER DÉPEND DE QUI RETIRE, ET DE RIEN D'AUTRE. Le vendeur a déclaré son
        // compte à l'inscription, le client l'a enregistré comme moyen de paiement — la
        // suite du handler ne fait aucune différence entre les deux.
        var account = await _payoutAccounts.ResolveAsync(
            withdrawal.OwnerType, withdrawal.OwnerId, cancellationToken);

        if (account is null)
        {
            return await FailAndRefundAsync(withdrawal, "Aucun compte de versement Mobile Money configuré.", cancellationToken);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // ON RÉCLAME LA LIGNE AVANT D'APPELER LE PRESTATAIRE. L'ORDRE EST TOUT.
        //
        // L'appel au PSP se faisait AVANT le moindre `SaveChanges`. Deux administrateurs
        // — ou un double-clic, ou une requête rejouée — chargeaient la même demande,
        // passaient tous deux le contrôle « en attente d'approbation », et déclenchaient
        // tous deux un virement Mobile Money. Deux versements pour un seul débit, et
        // aucun moyen de les rattraper : l'argent est parti chez l'opérateur.
        //
        // Écrire d'abord ferme les deux scénarios avec un seul geste :
        //
        //  • SIMULTANÉS — les deux ont lu la ligne « en attente » ; tous deux tentent
        //    d'écrire « en cours ». Le jeton de concurrence (`xmin`) en fait échouer un,
        //    AVANT son appel au PSP. Il lève, il ne paie pas.
        //  • SUCCESSIFS — le second lit la ligne déjà « en cours », `IsPendingApproval`
        //    est faux, il est refusé à la porte.
        //
        // Ni l'un ni l'autre ne marchait quand l'écriture venait après : le verrou ne
        // servait à rien s'il ne protégeait qu'un état déjà payé.
        //
        // CE QUE ÇA COÛTE, ET IL FAUT LE DIRE : si l'appel au PSP échoue par exception —
        // processus tué, réseau coupé — la demande reste « en cours » SANS référence
        // prestataire. `ReconcileWithdrawalsCommand` ignore ces lignes-là, faute de
        // pouvoir interroger FedaPay sans sa référence. Elles sont donc à reprendre à la
        // main, et `SentToPspAtUtc` — posé ici — est ce qui permet de les repérer par
        // leur âge. C'est un échange délibéré : un versement immobilisé et visible vaut
        // mieux qu'un versement envoyé deux fois.
        // ═════════════════════════════════════════════════════════════════════════
        // SANS NOTE. Ce passage ne signale rien d'anormal : il enregistre qu'on est
        // sur le point d'appeler le prestataire, et c'est `SentToPspAtUtc` — posé par
        // `MarkProcessing` — qui porte cette information. Le texte qui se trouvait ici
        // atterrissait dans `FailureReason`, que le BFF admin publie sous le nom
        // « anomaly » : le tableau de bord passait donc en alerte critique pendant la
        // durée de l'appel HTTP, sur le chemin nominal.
        withdrawal.MarkProcessing(null);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: withdrawal.Amount,
                Currency: withdrawal.Currency,
                BeneficiaryName: account.BeneficiaryName,
                Msisdn: account.AccountNumber,
                // L'opérateur DE L'ACTEUR détermine à lui seul le routage PSP (mode ET
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
                // `outcome.Error` PEUT ÊTRE NUL — le contrat l'admet, et un timeout pur
                // n'apporte aucun message. Sans ce repli, l'issue la plus incertaine
                // qui soit ressortait SANS anomalie, donc plus discrètement qu'un rejet
                // franc. On nomme l'incident nous-mêmes.
                withdrawal.MarkProcessing(
                    outcome.ProviderReference,
                    outcome.Error ?? "Issue indéterminée : le prestataire n'a pas confirmé la demande.");
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
