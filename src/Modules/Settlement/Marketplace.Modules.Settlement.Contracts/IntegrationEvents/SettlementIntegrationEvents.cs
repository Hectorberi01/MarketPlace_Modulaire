using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

/// <summary>Un reversement a été versé à un vendeur. Consommé par Notifications / comptabilité.</summary>
public sealed record PayoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid BatchId { get; init; }
    public required Guid PayoutId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// La cagnotte d'un client a été créditée du montant d'un remboursement de retour :
/// L'ARGENT EST ARRIVÉ.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CET ÉVÉNEMENT EXISTE, ET POURQUOI IL VA DE SETTLEMENT VERS RETURNS.
///
/// Le remboursement d'un retour se déroulait ainsi : le vendeur ou l'admin validait
/// le montant, la demande tombait dans une file, un humain ouvrait le tableau de bord
/// FedaPay, effectuait le versement à la main, revenait coller la référence. Chaque
/// remboursement coûtait donc un geste, et chaque geste oublié laissait un client sans
/// son argent pendant des jours.
///
/// La cagnotte supprime ce geste : créditer un solde interne est instantané, gratuit,
/// et réversible en écriture comptable. Mais c'est SETTLEMENT qui crédite, alors que
/// c'est RETURNS qui détient le statut de la demande — et Returns ne doit pas appeler
/// Settlement, ni l'inverse, en synchrone.
///
/// D'où cet événement : Returns annonce « remboursement validé », Settlement crédite
/// et annonce « cagnotte créditée », Returns clôt la demande. Chaque module ne connaît
/// que les contrats de l'autre, et le versement — devenu une écriture — ne peut plus
/// être oublié.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CustomerRefundCreditedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>
    /// Référence du mouvement, de la forme « wallet:{identifiant du portefeuille} ».
    ///
    /// Elle joue le rôle qu'avait la référence FedaPay : la PREUVE que l'argent est
    /// parti. Elle est simplement devenue vérifiable — le grand livre porte l'écriture
    /// correspondante, ce qu'aucune référence recopiée à la main ne garantissait.
    /// </summary>
    public required string LedgerReference { get; init; }
}

/// <summary>
/// Une course a été livrée et le coursier a été crédité de sa rémunération.
///
/// Émis APRÈS l'écriture au portefeuille, pas avant : c'est la confirmation que
/// l'argent est bien passé, et le service de dispatch peut s'en servir pour clore la
/// course de son côté.
///
/// DÉCLARÉ ICI ET PAS DANS Delivery.Contracts, bien qu'il parle d'un coursier : un
/// module déclare les événements qu'il PUBLIE, et c'est Settlement qui crédite. Le
/// ranger ailleurs enverrait chercher le publieur dans le mauvais module.
/// </summary>
public sealed record CourierEarningCreditedIntegrationEvent : IntegrationEvent
{
    public required Guid CourierId { get; init; }
    public required Guid OrderId { get; init; }
    /// <summary>
    /// Identifiant de la course dans le service de dispatch. C'est LA CLÉ
    /// D'IDEMPOTENCE du crédit : deux annonces de la même course ne paient qu'une fois.
    /// </summary>
    public required Guid DeliveryJobId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
