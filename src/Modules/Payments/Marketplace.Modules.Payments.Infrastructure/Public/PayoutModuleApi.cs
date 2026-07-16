using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Contracts;

namespace Marketplace.Modules.Payments.Infrastructure.Public;

/// <summary>
/// API publique de reversement : adapte l'instruction inter-modules vers le port
/// <see cref="IPayoutGateway"/> (FedaPay réel ou stub selon la configuration).
/// </summary>
internal sealed class PayoutModuleApi : IPayoutModuleApi
{
    private readonly IPayoutGateway _gateway;

    public PayoutModuleApi(IPayoutGateway gateway) => _gateway = gateway;

    public async Task<PayoutOutcome> SendMobileMoneyPayoutAsync(PayoutInstructionContract instruction, CancellationToken cancellationToken = default)
    {
        var result = await _gateway.SendAsync(
            new PayoutInstruction(
                instruction.Amount,
                instruction.Currency,
                new PayoutBeneficiary(instruction.BeneficiaryName, instruction.Msisdn, instruction.Provider),
                instruction.Reference),
            cancellationToken);

        return new PayoutOutcome(Map(result.Kind), result.ProviderReference, result.Error);
    }

    public async Task<PayoutProgress> GetPayoutProgressAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        var status = await _gateway.GetStatusAsync(providerReference, cancellationToken);
        return Map(status.Status);
    }

    public PayoutWebhookNotification ReadPayoutWebhook(string rawBody, string? signatureHeader)
    {
        var payoutEvent = _gateway.ParseWebhook(rawBody, signatureHeader);
        return new PayoutWebhookNotification(
            payoutEvent.IsPayoutEvent,
            payoutEvent.Verified,
            payoutEvent.ProviderReference,
            Map(payoutEvent.Status));
    }

    private static PayoutProgress Map(PayoutStatus status) => status switch
    {
        PayoutStatus.Pending => PayoutProgress.Pending,
        PayoutStatus.Started => PayoutProgress.Started,
        PayoutStatus.Processing => PayoutProgress.Processing,
        PayoutStatus.Sent => PayoutProgress.Sent,
        PayoutStatus.Failed => PayoutProgress.Failed,
        _ => PayoutProgress.Unknown
    };

    private static PayoutOutcomeStatus Map(PayoutOutcomeKind kind) => kind switch
    {
        PayoutOutcomeKind.Accepted => PayoutOutcomeStatus.Accepted,
        PayoutOutcomeKind.Failed => PayoutOutcomeStatus.Failed,
        _ => PayoutOutcomeStatus.Unknown
    };
}
