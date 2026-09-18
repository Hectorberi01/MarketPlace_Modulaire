using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Returns.Domain.Returns.Events;

/// <summary>Une demande de retour a été ouverte.</summary>
public sealed record ReturnRequestedDomainEvent(Guid ReturnRequestId, Guid OrderId, Guid OfferId, Guid BuyerId) : DomainEvent;

/// <summary>
/// Le remboursement est VALIDÉ — l'argent n'est pas encore parti.
///
/// Sert à prévenir l'acheteur que sa demande est acceptée (« versement en cours »),
/// et à faire apparaître l'opération dans la file des remboursements à exécuter du
/// back-office.
///
/// Ne contre-passe RIEN. Débiter le vendeur ici, ce serait le sanctionner pour un
/// versement qui peut encore échouer.
/// </summary>
public sealed record ReturnRefundApprovedDomainEvent(
    Guid ReturnRequestId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal RefundAmount,
    string Currency) : DomainEvent;

/// <summary>
/// L'argent est RÉELLEMENT PARTI (référence FedaPay à l'appui).
///
/// C'est le seul événement qui engage la comptabilité : il contre-passe le gain du
/// vendeur et restitue la commission de la plateforme.
/// </summary>
public sealed record ReturnRefundedDomainEvent(
    Guid ReturnRequestId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal RefundAmount,
    string Currency,
    string RefundReference) : DomainEvent;
