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
                new PayoutBeneficiary(instruction.BeneficiaryName, instruction.Msisdn, instruction.CountryCode),
                instruction.Reference),
            cancellationToken);

        return new PayoutOutcome(result.Success, result.ProviderReference, result.Error);
    }
}
