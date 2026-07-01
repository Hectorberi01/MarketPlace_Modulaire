using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Stub de reversement : simule un dépôt réussi sans toucher le réseau. Permet de
/// dérouler le payout vendeur de bout en bout sans clé FedaPay. Dès qu'une clé est
/// configurée, l'installer bascule sur <see cref="Real.FedaPayPayoutGateway"/>.
/// </summary>
public sealed class SimulatedPayoutGateway : IPayoutGateway
{
    public Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default)
        => Task.FromResult(new PayoutResult(Success: true, ProviderReference: $"sim_payout_{Guid.NewGuid():N}", Error: null));
}
