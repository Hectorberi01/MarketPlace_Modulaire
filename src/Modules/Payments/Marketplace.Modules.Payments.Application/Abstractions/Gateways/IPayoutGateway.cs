namespace Marketplace.Modules.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Bénéficiaire d'un reversement (vendeur) : nom + numéro Mobile Money + opérateur.
/// <paramref name="Provider"/> = opérateur du vendeur (ex. « MtnMomo », « MoovMoney »,
/// « Celtis »), traduit par le gateway en « mode » FedaPay (mtn_open/moov/sbin).
/// </summary>
public sealed record PayoutBeneficiary(string Name, string Msisdn, string CountryCode, string Provider);

/// <summary>Instruction de reversement (dépôt) vers un bénéficiaire.</summary>
public sealed record PayoutInstruction(decimal Amount, string Currency, PayoutBeneficiary Beneficiary, string Reference);

/// <summary>Résultat d'un reversement : référence PSP si succès, message sinon.</summary>
public sealed record PayoutResult(bool Success, string? ProviderReference, string? Error);

/// <summary>
/// Port d'un prestataire de DÉPÔT (payout / disbursement) — distinct de
/// <see cref="IPaymentGateway"/> qui encaisse depuis l'acheteur. Une implémentation
/// par PSP (FedaPay…) vit dans Infrastructure ; le métier ne dépend que de ce contrat.
/// </summary>
public interface IPayoutGateway
{
    /// <summary>Reverse un montant au bénéficiaire (Mobile Money).</summary>
    Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default);
}
