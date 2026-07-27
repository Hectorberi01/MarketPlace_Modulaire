using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

// ============================================================================
// Remboursement DIRECT d'un client, initié par l'admin sur une commande (hors flux
// « retour »). FedaPay ne remboursant pas une transaction via l'API, l'argent part
// par un PAYOUT Mobile Money vers le numéro du client, et le coût est débité du
// portefeuille plateforme (solde « refunds »). Statuts et gestion des issues
// indéterminées calqués sur les retraits vendeur : on ne rembourse jamais à
// l'aveugle un versement dont on ignore l'issue (risque de double versement).
// ============================================================================

/// <summary>
/// Rembourse un client sur une commande : débite la plateforme et déclenche un payout
/// FedaPay MoMo vers son numéro. Le montant est plafonné au total payé de la commande,
/// diminué des remboursements directs déjà effectués.
/// </summary>
public sealed record InitiateCustomerRefundCommand(
    Guid OrderId, decimal Amount, string Msisdn, string Provider, string Reason) : ICommand<CustomerRefundView>;

public sealed class InitiateCustomerRefundCommandValidator : AbstractValidator<InitiateCustomerRefundCommand>
{
    public InitiateCustomerRefundCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant doit être positif.");
        RuleFor(c => c.Msisdn).NotEmpty().WithMessage("Le numéro Mobile Money du client est requis.");
        RuleFor(c => c.Provider).NotEmpty().WithMessage("L'opérateur est requis.");
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Un motif est requis.");
    }
}

internal sealed class InitiateCustomerRefundCommandHandler : ICommandHandler<InitiateCustomerRefundCommand, CustomerRefundView>
{
    private readonly ICustomerRefundRepository _refunds;
    private readonly WalletMutations _wallets;
    private readonly IOrderingModuleApi _orders;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public InitiateCustomerRefundCommandHandler(
        ICustomerRefundRepository refunds,
        WalletMutations wallets,
        IOrderingModuleApi orders,
        IPayoutModuleApi payouts,
        ISettlementUnitOfWork unitOfWork)
    {
        _refunds = refunds;
        _wallets = wallets;
        _orders = orders;
        _payouts = payouts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerRefundView>> Handle(InitiateCustomerRefundCommand command, CancellationToken cancellationToken)
    {
        var order = await _orders.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<CustomerRefundView>(Error.NotFound("settlement.order_not_found", "Commande introuvable."));
        }

        // On ne rembourse qu'une commande RÉELLEMENT ENCAISSÉE (Confirmed / Delivered).
        if (order.Status is not ("Confirmed" or "Delivered"))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.order_not_refundable", "Seule une commande encaissée (confirmée ou livrée) peut être remboursée ici."));
        }

        // L'opérateur doit être un Mobile Money routable, sinon le payout échouera —
        // autant refuser avant de débiter la plateforme.
        if (!WalletPayout.IsMobileMoney(command.Provider))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.provider_unsupported", "Opérateur Mobile Money non pris en charge (mtnmomo, moovmoney, celtis)."));
        }

        // Plafond : total payé (commande + livraison) moins les remboursements DIRECTS
        // déjà effectués sur cette commande.
        // ⚠️ Ne nette PAS encore les remboursements issus du flux « retour ». À suivre si
        // les deux flux doivent partager un plafond commun.
        var paidTotal = order.GrandTotal + order.ShippingFee;
        var alreadyRefunded = await _refunds.SumActiveForOrderAsync(command.OrderId, cancellationToken);
        var remaining = paidTotal - alreadyRefunded;
        if (command.Amount > remaining)
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.refund_exceeds_paid",
                $"Le remboursement dépasse le montant remboursable ({remaining} {order.Currency})."));
        }

        var refund = CustomerRefund.Create(
            command.OrderId, order.BuyerId, command.Amount, order.Currency, command.Reason, command.Msisdn, command.Provider);
        await _refunds.AddAsync(refund, cancellationToken);

        // Débit plateforme AVANT le versement (contre-passé si le PSP refuse), pour ne
        // jamais laisser un payout parti sans écriture comptable en regard.
        await _wallets.AccrueCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: refund.Amount,
                Currency: refund.Currency,
                BeneficiaryName: "Client",
                Msisdn: refund.Msisdn,
                Provider: refund.Provider,
                Reference: $"refund:{refund.Id}"),
            cancellationToken);

        switch (outcome.Status)
        {
            // Rejet DÉFINITIF : rien n'est parti → on contre-passe le débit plateforme.
            case PayoutOutcomeStatus.Failed:
                refund.Fail(outcome.Error ?? "Versement refusé par le prestataire.");
                await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure<CustomerRefundView>(Error.Failure("settlement.refund_failed", refund.FailureReason!));

            // Issue INDÉTERMINÉE (timeout/5xx) : le versement est peut-être parti. On NE
            // contre-passe PAS — la réconciliation tranchera.
            case PayoutOutcomeStatus.Unknown:
                refund.MarkProcessing(outcome.ProviderReference, outcome.Error);
                break;

            // Accepté = créé + démarré chez FedaPay (« started », pas encore « sent ») :
            // la réconciliation clôturera en Completed sur le statut « sent ».
            default:
                refund.MarkProcessing(outcome.ProviderReference);
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(refund);
    }
}

/// <summary>
/// Réconcilie les remboursements client « en cours » avec le statut RÉEL du dépôt chez
/// le PSP. Même logique que la réconciliation des retraits : Sent → Completed, Failed →
/// contre-passation du débit plateforme, sinon on ne touche à rien. Idempotent.
/// </summary>
public sealed record ReconcileCustomerRefundsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ReconcileCustomerRefundsCommandHandler : ICommandHandler<ReconcileCustomerRefundsCommand, int>
{
    private readonly ICustomerRefundRepository _refunds;
    private readonly IPayoutModuleApi _payouts;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ReconcileCustomerRefundsCommandHandler(
        ICustomerRefundRepository refunds,
        IPayoutModuleApi payouts,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork)
    {
        _refunds = refunds;
        _payouts = payouts;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(ReconcileCustomerRefundsCommand command, CancellationToken cancellationToken)
    {
        var processing = await _refunds.ListProcessingAsync(cancellationToken);
        var settled = 0;

        foreach (var refund in processing.Take(command.BatchSize))
        {
            // Sans référence PSP (timeout avant l'identifiant), on ne peut rien interroger
            // ni rembourser à l'aveugle : arbitrage humain.
            if (string.IsNullOrWhiteSpace(refund.ProviderRef))
            {
                continue;
            }

            var progress = await _payouts.GetPayoutProgressAsync(refund.ProviderRef!, cancellationToken);

            switch (progress)
            {
                case PayoutProgress.Sent:
                    refund.Complete(refund.ProviderRef);
                    settled++;
                    break;

                case PayoutProgress.Failed:
                    refund.Fail("Versement refusé par le prestataire (statut « failed »).");
                    await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                    settled++;
                    break;

                // pending / started / processing / unknown : encore en vol → on ne touche à rien.
                default:
                    break;
            }
        }

        if (settled > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return settled;
    }
}
