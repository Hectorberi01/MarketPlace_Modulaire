using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Stub de reversement : simule un dépôt sans toucher le réseau. Permet de dérouler
/// le payout vendeur de bout en bout sans clé FedaPay. Dès qu'une clé est configurée,
/// l'installer bascule sur <see cref="Real.FedaPayPayoutGateway"/>.
///
/// Le stub reproduit le CYCLE RÉEL : la demande est « Accepted » (= démarrée), puis
/// la réconciliation lit « Sent ». Sans cela, la simulation validerait un chemin de
/// code que la production n'emprunte jamais — et masquerait les bugs de réconciliation.
/// </summary>
public sealed class SimulatedPayoutGateway : IPayoutGateway
{
    /// <summary>Vrai, et c'est tout l'intérêt : aucun argent ne part d'ici.</summary>
    public bool IsSimulated => true;

    public Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default)
        => Task.FromResult(PayoutResult.Accepted($"sim_payout_{Guid.NewGuid():N}"));

    /// <summary>En simulation, un dépôt demandé est toujours considéré comme arrivé.</summary>
    public Task<PayoutStatusResult> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new PayoutStatusResult(PayoutStatus.Sent, null));

    /// <summary>
    /// Sans PSP réel, aucun webhook de dépôt n'arrive : tout payload est « non-dépôt »
    /// et repart vers le flux paiement. Surtout ne pas simuler un événement vérifié —
    /// cela permettrait de clôturer un retrait avec un payload arbitraire.
    /// </summary>
    public PayoutWebhookEvent ParseWebhook(string rawBody, string? signatureHeader)
        => PayoutWebhookEvent.NotPayout;
}
