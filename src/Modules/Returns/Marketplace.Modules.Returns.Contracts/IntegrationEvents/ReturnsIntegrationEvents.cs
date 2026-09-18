using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Returns.Contracts.IntegrationEvents;

/// <summary>
/// Un remboursement a été VALIDÉ, mais l'argent n'est pas encore parti.
///
/// Consommé par Notifications : on rassure l'acheteur (« votre remboursement est
/// accepté, le versement est en cours »). C'est ce message qui évite qu'il ouvre un
/// litige pendant les quelques heures où l'opération attend d'être exécutée.
///
/// N'engage AUCUNE écriture comptable.
/// </summary>
public sealed record ReturnRefundApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// L'argent a RÉELLEMENT été versé à l'acheteur (référence FedaPay à l'appui).
///
/// Consommé par Settlement (contre-passation du gain vendeur + restitution de la
/// commission) et Notifications (acheteur et vendeur).
///
/// Cet événement était déjà publié — et PERSONNE ne l'écoutait. C'est ce silence
/// qui faisait qu'un remboursement ne remboursait rien, ne débitait personne, et
/// n'informait qui que ce soit.
/// </summary>
public sealed record ReturnRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Référence du versement chez FedaPay : la preuve que l'argent est parti.</summary>
    public required string RefundReference { get; init; }
}
