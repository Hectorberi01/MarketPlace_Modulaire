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
