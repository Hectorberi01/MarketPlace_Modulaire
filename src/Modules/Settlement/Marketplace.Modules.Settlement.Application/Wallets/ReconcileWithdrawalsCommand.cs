using Microsoft.Extensions.Logging;
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
    /// <summary>
    /// Au-delà de ce délai, un retrait « en cours » sans référence prestataire n'est plus
    /// une latence : c'est une anomalie qui attend un humain.
    /// </summary>
    private static readonly TimeSpan StrandedAfter = TimeSpan.FromHours(1);

    private readonly IWithdrawalRepository _withdrawals;
    private readonly IPayoutModuleApi _payouts;
    private readonly WithdrawalSettlement _settlement;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<ReconcileWithdrawalsCommandHandler> _logger;

    public ReconcileWithdrawalsCommandHandler(
        IWithdrawalRepository withdrawals,
        IPayoutModuleApi payouts,
        WithdrawalSettlement settlement,
        ISettlementUnitOfWork unitOfWork,
        ILogger<ReconcileWithdrawalsCommandHandler> logger)
    {
        _withdrawals = withdrawals;
        _payouts = payouts;
        _settlement = settlement;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(ReconcileWithdrawalsCommand command, CancellationToken cancellationToken)
    {
        var pending = await _withdrawals.ListProcessingForReconciliationAsync(command.BatchSize, cancellationToken);
        var settled = 0;

        foreach (var withdrawal in pending)
        {
            // ═════════════════════════════════════════════════════════════════════
            // SANS RÉFÉRENCE PRESTATAIRE, ON NE PEUT RIEN INTERROGER — MAIS ON PEUT LE
            // DIRE.
            //
            // `GetPayoutProgressAsync` prend la référence de FedaPay ; on ne l'a pas.
            // Rembourser à l'aveugle est exclu : le versement est peut-être parti. Le
            // retrait reste donc « en cours », pour arbitrage humain.
            //
            // CE QUI CHANGE : le `continue` était MUET. Ces lignes étaient repassées et
            // réignorées toutes les deux minutes, indéfiniment, sans que rien ne signale
            // qu'un client attend son argent. La seule surface était une liste d'écran
            // qu'il fallait penser à ouvrir.
            //
            // Le cas est devenu plus fréquent depuis que l'approbation réclame la ligne
            // AVANT d'appeler le prestataire — c'était le prix à payer pour supprimer le
            // double virement, et il se paie ici, en visibilité.
            //
            // On ne journalise qu'au-delà d'une heure : en deçà, une écriture en cours
            // est normale, et alerter toutes les deux minutes sur un cas normal apprend
            // surtout à ignorer l'alerte.
            // ═════════════════════════════════════════════════════════════════════
            if (string.IsNullOrWhiteSpace(withdrawal.ProviderRef))
            {
                var age = DateTime.UtcNow - (withdrawal.SentToPspAtUtc ?? withdrawal.CreatedAtUtc);
                if (age > StrandedAfter)
                {
                    _logger.LogCritical(
                        "RETRAIT IMMOBILISÉ depuis {Age}. Retrait {WithdrawalId}, {Amount} {Currency} pour "
                        + "{OwnerType} {OwnerId} : « en cours » SANS référence prestataire — impossible à "
                        + "réconcilier automatiquement. Le solde du client est débité et l'argent n'est ni "
                        + "versé ni rendu. À trancher à la main dans le tableau de bord FedaPay.",
                        age, withdrawal.Id.Value, withdrawal.Amount, withdrawal.Currency,
                        withdrawal.OwnerType, withdrawal.OwnerId);
                }

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
