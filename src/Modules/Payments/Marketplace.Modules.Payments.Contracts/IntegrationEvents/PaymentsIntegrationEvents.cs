using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>Paiement encaissé. Consommé par Ordering (confirmation de la commande).</summary>
public sealed record PaymentCapturedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
}

/// <summary>Paiement échoué. Consommé par Ordering (annulation + libération du stock).</summary>
public sealed record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Paiement remboursé. Consommé par Notifications, la comptabilité et Analytics.
///
/// <c>Amount</c> et <c>Currency</c> VOYAGENT AVEC L'ÉVÉNEMENT. Le domaine les portait
/// déjà (<c>PaymentRefundedDomainEvent</c>) et le contrat les laissait tomber : tout
/// consommateur voulant savoir COMBIEN avait été rendu devait rappeler le module
/// Payments, ou deviner. Analytics, qui doit retrancher ce montant du chiffre
/// d'affaires, aurait dû deviner.
///
/// C'est le montant réellement remboursé, c'est-à-dire <c>Payment.Amount</c> —
/// soit le montant DÛ de la commande, cagnotte déduite, et non son total.
/// </summary>
public sealed record PaymentRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }

    // ═════════════════════════════════════════════════════════════════════════════
    // PAS `required`, ET C'EST DÉLIBÉRÉ.
    //
    // L'outbox conserve ses messages en JSON et les relit après déploiement. Un
    // `PaymentRefundedIntegrationEvent` sérialisé AVANT l'ajout de ces deux membres
    // n'a pas les champs : System.Text.Json honore `required` et lèverait à chaque
    // tentative, jusqu'à la lettre morte — le message perdrait sa notification et son
    // écriture comptable, pour un champ qui n'existait pas quand il a été écrit.
    //
    // Un ancien message se lit donc `Amount = 0` et `Currency = ""`, ce que le
    // consommateur Analytics écarte par son contrôle de devise, en le journalisant.
    // ═════════════════════════════════════════════════════════════════════════════
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}
