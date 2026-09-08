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
