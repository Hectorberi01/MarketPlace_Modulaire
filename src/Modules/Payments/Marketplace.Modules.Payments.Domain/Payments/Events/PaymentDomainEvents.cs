using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Payments.Domain.Payments.Events;

/// <summary>Un paiement a été initié pour une commande.</summary>
public sealed record PaymentInitiatedDomainEvent(Guid PaymentId, Guid OrderId, decimal Amount, string Currency) : DomainEvent;

/// <summary>Le paiement a été encaissé — déclenche la confirmation de la commande.
/// Porte provider/méthode/montant/devise pour les métriques (aucune donnée perso).</summary>
public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId, Guid OrderId, string Provider, string Method, decimal Amount, string Currency) : DomainEvent;

/// <summary>Le paiement a échoué — déclenche l'annulation de la commande.</summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId, Guid OrderId, string Reason, string Provider, string Method, string Currency) : DomainEvent;

/// <summary>Le paiement a été remboursé.</summary>
public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId, Guid OrderId, string Provider, decimal Amount, string Currency) : DomainEvent;
