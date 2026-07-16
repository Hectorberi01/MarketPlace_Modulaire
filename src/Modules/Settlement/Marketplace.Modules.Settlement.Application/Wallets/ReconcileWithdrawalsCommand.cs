using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Réconcilie les retraits « en cours » avec le statut RÉEL du dépôt chez le PSP.
///
/// Pourquoi c'est indispensable : accepter un <c>PUT /payouts/start</c> ne signifie que
/// « started » chez FedaPay. Le cycle continue (processing → sent | failed). Sans cette
/// réconciliation, un versement échoué laisserait le retrait marqué « payé » et le
/// vendeur débité — sans jamais avoir reçu l'argent, et sans alerte.
///
/// Le webhook fait le même travail en temps réel ; ce balayage reste le FILET DE
/// SÉCURITÉ, car un webhook peut se perdre, être rejeté, ou n'être jamais envoyé.
/// Les deux passent par <see cref="WithdrawalSettlement"/>, qui est idempotent.
/// </summary>
public sealed record ReconcileWithdrawalsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ReconcileWithdrawalsCommandHandler : ICommandHandler<ReconcileWithdrawalsCommand, int>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IPayoutModuleApi _payouts;
    private readonly WithdrawalSettlement _settlement;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public ReconcileWithdrawalsCommandHandler(
        IWithdrawalRepository withdrawals,
        IPayoutModuleApi payouts,
        WithdrawalSettlement settlement,
        ISettlementUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _payouts = payouts;
        _settlement = settlement;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(ReconcileWithdrawalsCommand command, CancellationToken cancellationToken)
    {
        var pending = await _withdrawals.ListProcessingForReconciliationAsync(command.BatchSize, cancellationToken);
        var settled = 0;

        foreach (var withdrawal in pending)
        {
            // Sans référence PSP, on ne peut RIEN interroger : le dépôt a peut-être été
            // créé sans qu'on récupère son identifiant (timeout). On laisse le retrait en
            // « Processing » pour arbitrage humain — surtout ne pas rembourser à l'aveugle.
            if (string.IsNullOrWhiteSpace(withdrawal.ProviderRef))
            {
                continue;
            }

            var progress = await _payouts.GetPayoutProgressAsync(withdrawal.ProviderRef!, cancellationToken);

            if (await _settlement.ApplyAsync(withdrawal, progress, cancellationToken))
            {
                settled++;
            }
        }

        if (settled > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return settled;
    }
}
