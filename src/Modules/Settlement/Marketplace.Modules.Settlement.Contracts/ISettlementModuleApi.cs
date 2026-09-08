namespace Marketplace.Modules.Settlement.Contracts;

/// <summary>
/// API in-process publique du module Settlement — la première.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI SETTLEMENT S'OUVRE ENFIN, ET POURQUOI EN ÉCRITURE.
///
/// Settlement était le seul des seize modules sans API publique : rien n'avait jamais
/// eu besoin de lui parler, il ne faisait qu'écouter des événements. Dépenser la
/// cagnotte au moment de payer change cela — et ne peut pas passer par un événement,
/// parce que la question posée est « puis-je engager cet argent MAINTENANT ? » et que
/// la réponse doit arriver avant que la commande ne parte au paiement.
///
/// LA FORME EST CELLE D'IInventoryModuleApi, DÉLIBÉRÉMENT.
///
/// Réserver, confirmer, libérer : c'est exactement le contrat que le Saga de commande
/// tient déjà avec le stock, et pour la même raison — un engagement pris avant le
/// paiement doit pouvoir être défait si le paiement échoue. Reprendre ce vocabulaire
/// évite d'inventer une seconde façon de compenser dans le même orchestrateur, là où
/// une divergence se paierait en argent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ISettlementModuleApi
{
    /// <summary>
    /// Ce que ce client peut engager sur une commande, ici et maintenant : son solde
    /// DISPONIBLE, hors montants déjà réservés sur une autre commande en cours.
    /// </summary>
    Task<decimal> GetSpendableCreditAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Engage une part de la cagnotte sur une commande : déplace le montant du
    /// disponible vers le RÉSERVÉ, et renvoie ce qui a RÉELLEMENT été déplacé.
    ///
    /// Le retour peut être inférieur au montant demandé — plafonné au solde — et zéro
    /// si le client n'a rien. Ce n'est pas une erreur : entre l'affichage du panier et
    /// la validation, un autre onglet a pu engager la même cagnotte. L'appelant impute
    /// à la commande ce qui a été réservé, pas ce qu'il avait demandé.
    /// </summary>
    Task<decimal> TryReserveCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consomme la réservation : l'argent est dépensé, il quitte le portefeuille.
    /// Appelé à la confirmation de la commande. Idempotent.
    /// </summary>
    Task ConfirmCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// COMPENSATION : rend au disponible une réservation qui n'aboutira pas (paiement
    /// échoué, commande annulée, stock manquant). Idempotent.
    ///
    /// Sans elle, une cagnotte engagée sur une commande morte resterait bloquée dans le
    /// réservé — invisible pour le client, ni dépensable ni retirable, et sans qu'aucun
    /// écran ne puisse expliquer où est passé son argent.
    /// </summary>
    Task ReleaseCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default);
}
