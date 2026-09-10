namespace Marketplace.Modules.Payments.Contracts;

/// <summary>
/// Instruction de reversement Mobile Money exposée aux autres modules (Settlement).
/// Le <paramref name="Provider"/> (opérateur du vendeur) détermine à lui seul le
/// routage PSP — mode ET pays. Il n'y a donc plus de code pays à fournir : le passer
/// « en dur » était précisément le bug qui envoyait tous les numéros vers le Bénin.
/// </summary>
public sealed record PayoutInstructionContract(
    decimal Amount,
    string Currency,
    string BeneficiaryName,
    string Msisdn,
    string Provider,
    string Reference);

/// <summary>
/// Issue d'une demande de reversement :
/// <b>Accepted</b> = demandé au PSP (PAS encore reçu par le vendeur) ;
/// <b>Failed</b> = rejet définitif (remboursement sûr) ;
/// <b>Unknown</b> = indéterminé — NE JAMAIS rembourser, l'argent est peut-être parti
/// (rembourser autoriserait une seconde validation, donc un double versement).
/// </summary>
public enum PayoutOutcomeStatus
{
    Accepted = 0,
    Failed = 1,
    Unknown = 2
}

/// <summary>Résultat d'une demande de reversement.</summary>
public sealed record PayoutOutcome(PayoutOutcomeStatus Status, string? ProviderReference, string? Error);

/// <summary>Statut réel d'un dépôt chez le PSP. Seul <see cref="Sent"/> prouve le versement.</summary>
public enum PayoutProgress
{
    Pending = 0,
    Started = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5
}

/// <summary>
/// Notification de webhook concernant un DÉPÔT, normalisée pour les autres modules.
///
/// <paramref name="IsPayoutEvent"/> distingue un événement de dépôt d'un événement de
/// paiement : le PSP poste les deux sur la même URL, et leurs identifiants numériques
/// peuvent coïncider. Traiter l'un pour l'autre marquerait un paiement échoué à cause
/// d'un versement annulé.
/// </summary>
public sealed record PayoutWebhookNotification(bool IsPayoutEvent, bool Verified, string? ProviderReference, PayoutProgress Progress);

/// <summary>
/// API publique de reversement du module Payments. Permet à Settlement de
/// déclencher un dépôt vers le compte Mobile Money d'un vendeur sans connaître le
/// PSP (FedaPay) ni dépendre de l'infrastructure de paiement.
/// </summary>
public interface IPayoutModuleApi
{
    /// <summary>
    /// Les versements de cette instance sont-ils SIMULÉS ?
    ///
    /// Vrai quand aucun argent ne part : le cycle se déroule entièrement, le retrait
    /// est marqué « envoyé » avec une référence fictive, et le vendeur est débité. Un
    /// écran d'administration qui propose de valider un retrait DOIT le dire avant,
    /// pas après.
    /// </summary>
    bool PayoutsAreSimulated { get; }

    Task<PayoutOutcome> SendMobileMoneyPayoutAsync(PayoutInstructionContract instruction, CancellationToken cancellationToken = default);

    /// <summary>Statut réel d'un dépôt déjà demandé (réconciliation des retraits « en cours »).</summary>
    Task<PayoutProgress> GetPayoutProgressAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconnaît et vérifie un webhook de dépôt (signature comprise). Renvoie
    /// <c>IsPayoutEvent = false</c> si le payload concerne un paiement : l'appelant
    /// doit alors le confier au flux paiement, et surtout pas au flux retrait.
    /// </summary>
    PayoutWebhookNotification ReadPayoutWebhook(string rawBody, string? signatureHeader);
}
