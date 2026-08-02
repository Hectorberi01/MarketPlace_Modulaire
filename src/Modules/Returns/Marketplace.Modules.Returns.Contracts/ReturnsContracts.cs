namespace Marketplace.Modules.Returns.Contracts;

/// <summary>Vue publique d'une demande de retour.</summary>
public sealed record ReturnRequestSummary(
    Guid Id,
    Guid OrderId,
    Guid OfferId,
    Guid BuyerId,
    Guid SellerId,
    string Reason,
    string Status,
    string Currency,

    /// <summary>
    /// PLAFOND remboursable — total de la ligne de commande, figé à la création.
    ///
    /// À ne pas confondre avec <see cref="RefundAmount"/>, qui est le montant
    /// effectivement décidé (null tant qu'aucun remboursement n'a été validé).
    /// C'est cette confusion qui a empêché l'application vendeur de borner sa saisie :
    /// elle n'avait à sa disposition que le second, et l'a pris pour le premier.
    ///
    /// Vaut 0 pour les retours antérieurs à ce champ : traiter alors comme inconnu.
    /// </summary>
    decimal RefundableAmount,

    decimal? RefundAmount,
    string? Carrier,
    string? TrackingNumber,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>
/// Un remboursement effectif (retour au statut Refunded) : commande concernée,
/// montant remboursé et date du remboursement. Utilisé par le relevé finances.
/// </summary>
public sealed record SellerRefundLine(
    Guid OrderId,
    decimal RefundAmount,
    string Currency,
    DateTime RefundedAtUtc);

/// <summary>
/// Un remboursement VALIDÉ dont l'argent n'est PAS ENCORE PARTI.
///
/// C'est une ligne de la file de travail de l'administrateur : il doit exécuter ce
/// versement dans le tableau de bord FedaPay (qui n'expose aucune API de
/// remboursement), puis en saisir la référence pour clore l'opération.
///
/// <para>
/// <c>ApprovedAtUtc</c> n'est pas décoratif : c'est lui qui dit depuis combien de
/// temps le client attend. Une ligne vieille de trois jours n'est plus une tâche,
/// c'est un litige.
/// </para>
/// </summary>
public sealed record PendingRefundLine(
    Guid ReturnRequestId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal RefundAmount,
    string Currency,
    DateTime ApprovedAtUtc);
