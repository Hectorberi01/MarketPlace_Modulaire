using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Settlement.Domain.Wallets.Events;

/// <summary>
/// Un remboursement client direct a ABOUTI : l'argent est chez le client.
///
/// « Abouti » et non « décidé » : la jambe espèces ne l'émet qu'à la confirmation du
/// prestataire, la jambe cagnotte dès sa création puisque le crédit d'un solde interne
/// est immédiat. Une décision d'administrateur qui échouerait chez l'opérateur ne doit
/// rien retrancher au chiffre d'affaires.
/// </summary>
public sealed record CustomerRefundSettledDomainEvent(
    Guid CustomerRefundId,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Channel) : DomainEvent;
