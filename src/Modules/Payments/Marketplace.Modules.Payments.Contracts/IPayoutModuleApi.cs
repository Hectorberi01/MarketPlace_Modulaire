namespace Marketplace.Modules.Payments.Contracts;

/// <summary>Instruction de reversement Mobile Money exposée aux autres modules (Settlement).</summary>
public sealed record PayoutInstructionContract(
    decimal Amount,
    string Currency,
    string BeneficiaryName,
    string Msisdn,
    string CountryCode,
    string Provider,
    string Reference);

/// <summary>Résultat d'un reversement.</summary>
public sealed record PayoutOutcome(bool Success, string? ProviderReference, string? Error);

/// <summary>
/// API publique de reversement du module Payments. Permet à Settlement de
/// déclencher un dépôt vers le compte Mobile Money d'un vendeur sans connaître le
/// PSP (FedaPay) ni dépendre de l'infrastructure de paiement.
/// </summary>
public interface IPayoutModuleApi
{
    Task<PayoutOutcome> SendMobileMoneyPayoutAsync(PayoutInstructionContract instruction, CancellationToken cancellationToken = default);
}
