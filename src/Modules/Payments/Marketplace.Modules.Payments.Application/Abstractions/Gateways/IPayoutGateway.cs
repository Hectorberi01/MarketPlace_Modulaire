namespace Marketplace.Modules.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Bénéficiaire d'un reversement (vendeur) : nom + numéro Mobile Money + opérateur.
/// <paramref name="Provider"/> = opérateur du vendeur (ex. « MtnMomo », « MoovMoney »,
/// « Celtis »). C'est LUI qui détermine à la fois le « mode » FedaPay (mtn_open,
/// moov, sbin…) ET le pays : les modes FedaPay encodent les deux (mtn_ci = MTN Côte
/// d'Ivoire, wave_sn = Wave Sénégal…). Le gateway REFUSE un opérateur qu'il ne sait
/// pas router, plutôt que de deviner un pays et de mal acheminer les fonds.
/// </summary>
public sealed record PayoutBeneficiary(string Name, string Msisdn, string Provider);

/// <summary>Instruction de reversement (dépôt) vers un bénéficiaire.</summary>
public sealed record PayoutInstruction(decimal Amount, string Currency, PayoutBeneficiary Beneficiary, string Reference);

/// <summary>
/// Issue d'une demande de reversement. Cette distinction est CRITIQUE pour la
/// comptabilité :
/// <list type="bullet">
///   <item><b>Accepted</b> — le PSP a créé ET démarré le dépôt. Attention : cela ne
///     signifie que « started », PAS « l'argent est arrivé ». Le statut final
///     (sent/failed) doit être confirmé par réconciliation.</item>
///   <item><b>Failed</b> — rejet DÉFINITIF et sans ambiguïté (validation, opérateur non
///     supporté, 4xx). Aucun argent n'est parti : on peut recréditer sans risque.</item>
///   <item><b>Unknown</b> — issue INDÉTERMINÉE (timeout, 5xx, réponse illisible). Le dépôt
///     est peut-être parti. Il ne faut SURTOUT PAS recréditer : cela autoriserait une
///     seconde validation, donc un DOUBLE VERSEMENT. Seule la réconciliation tranche.</item>
/// </list>
/// </summary>
public enum PayoutOutcomeKind
{
    Accepted = 0,
    Failed = 1,
    Unknown = 2
}

/// <summary>Résultat d'une demande de reversement.</summary>
public sealed record PayoutResult(PayoutOutcomeKind Kind, string? ProviderReference, string? Error)
{
    public static PayoutResult Accepted(string providerReference) => new(PayoutOutcomeKind.Accepted, providerReference, null);
    public static PayoutResult Failed(string error) => new(PayoutOutcomeKind.Failed, null, error);
    public static PayoutResult Unknown(string error, string? providerReference = null) => new(PayoutOutcomeKind.Unknown, providerReference, error);
}

/// <summary>
/// Cycle de vie d'un dépôt chez le PSP (FedaPay : pending → started → processing →
/// sent | failed). Seul <see cref="Sent"/> prouve que le vendeur a reçu l'argent.
/// </summary>
public enum PayoutStatus
{
    Pending = 0,
    Started = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5
}

/// <summary>Statut courant d'un dépôt, interrogé auprès du PSP.</summary>
public sealed record PayoutStatusResult(PayoutStatus Status, string? Error);

/// <summary>
/// Événement de webhook concernant un DÉPÔT (payout), normalisé.
///
/// <paramref name="IsPayoutEvent"/> est la garde essentielle : le PSP poste les
/// événements de paiement ET de dépôt sur la MÊME URL. Or les identifiants FedaPay
/// sont des entiers propres à chaque type d'entité — le dépôt n°4212 et la
/// transaction n°4212 coexistent. Confondre les deux permettrait à un événement de
/// dépôt d'aller marquer un PAIEMENT comme échoué. On ne traite donc un événement
/// que si son nom l'identifie explicitement comme un dépôt.
/// </summary>
public sealed record PayoutWebhookEvent(bool IsPayoutEvent, bool Verified, string? ProviderReference, PayoutStatus Status)
{
    /// <summary>Événement qui ne concerne pas un dépôt : à laisser au flux paiement.</summary>
    public static readonly PayoutWebhookEvent NotPayout = new(false, false, null, PayoutStatus.Unknown);

    /// <summary>Événement de dépôt dont la signature est invalide : à REJETER (401).</summary>
    public static readonly PayoutWebhookEvent Unsigned = new(true, false, null, PayoutStatus.Unknown);
}

/// <summary>
/// Port d'un prestataire de DÉPÔT (payout / disbursement) — distinct de
/// <see cref="IPaymentGateway"/> qui encaisse depuis l'acheteur. Une implémentation
/// par PSP (FedaPay…) vit dans Infrastructure ; le métier ne dépend que de ce contrat.
/// </summary>
public interface IPayoutGateway
{
    /// <summary>
    /// Demande un reversement au bénéficiaire (Mobile Money). Un retour
    /// <see cref="PayoutOutcomeKind.Accepted"/> ne signifie PAS que l'argent est arrivé :
    /// il faut confirmer via <see cref="GetStatusAsync"/>.
    /// </summary>
    Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Interroge le PSP sur le statut réel d'un dépôt déjà créé (réconciliation).</summary>
    Task<PayoutStatusResult> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconnaît et vérifie un webhook de DÉPÔT. Renvoie <see cref="PayoutWebhookEvent.NotPayout"/>
    /// si le payload concerne autre chose (paiement) : l'appelant le laissera alors au
    /// flux paiement. Ne lève jamais : un payload illisible est simplement « non-dépôt ».
    /// </summary>
    PayoutWebhookEvent ParseWebhook(string rawBody, string? signatureHeader);
}
