namespace Marketplace.Modules.Returns.Contracts;

/// <summary>
/// API in-process publique du module Returns.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE EXISTE POUR UNE SEULE RAISON : DEUX CIRCUITS DE REMBOURSEMENT QUI NE SE
/// VOYAIENT PAS.
///
/// Une commande peut être remboursée par deux chemins indépendants :
///
///   • le RETOUR — l'acheteur renvoie un article, le vendeur valide, la cagnotte est
///     créditée et le vendeur contre-passé ;
///   • le REMBOURSEMENT DIRECT — l'administrateur verse en Mobile Money, à la charge
///     de la plateforme.
///
/// Chacun plafonnait correctement… sur ses propres écritures. Le remboursement direct
/// bornait au total de la commande moins les remboursements directs déjà faits ; le
/// retour bornait au montant de la ligne. Ni l'un ni l'autre ne regardait l'autre
/// table. Sur une même commande, la plateforme pouvait donc verser le total en espèces
/// ET le total en cagnotte — environ deux fois la valeur de la commande, chaque
/// contrôle passant proprement de son côté.
///
/// Le commentaire de `InitiateCustomerRefundCommandHandler` avait vu le trou et l'avait
/// remis à plus tard : « NE NETTE PAS les remboursements issus du flux retour. À suivre
/// si les deux flux doivent partager un plafond commun. » Cette interface est ce
/// « plus tard ».
///
/// POURQUOI UNE API PLUTÔT QU'UNE TABLE PARTAGÉE : chaque module garde ses données.
/// Settlement demande une somme à Returns, Returns demande une somme à Settlement, et
/// aucun des deux ne lit la base de l'autre. C'est la forme qu'ont déjà les quinze
/// autres API de module.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IReturnsModuleApi
{
    /// <summary>
    /// Somme des remboursements ENGAGÉS sur une commande par le circuit « retour » :
    /// validés (versement en attente) et versés.
    ///
    /// Un remboursement validé compte dès sa décision, pas à son décaissement : sinon
    /// un remboursement direct autorisé pendant l'exécution d'un versement de retour
    /// verrait un plafond encore intact.
    /// </summary>
    Task<decimal> SumCommittedRefundsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
