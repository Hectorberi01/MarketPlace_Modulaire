namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE QU'UNE BOUTIQUE A LE DROIT DE FAIRE, SELON SON STATUT.
///
/// Un seul endroit, et il est dans les CONTRATS : la règle doit être lisible par le
/// BFF vendeur comme par les modules qui l'appliquent, sans que l'un dépende des
/// entrailles de l'autre.
///
/// POURQUOI CE FICHIER EXISTE.
///
/// Le droit de vendre n'était vérifié qu'à UN endroit de tout le dépôt — la création
/// d'une offre. `seller.Status` n'apparaissait nulle part ailleurs qu'en affichage.
/// Un vendeur SUSPENDU ne pouvait donc pas créer une offre neuve, mais il pouvait
/// republier celles qu'il avait en pause, baisser ses prix, appliquer des remises,
/// modifier ses fiches, en créer d'autres — et demander un virement.
///
/// Le contrôle était posé sur la porte de service et pas sur la porte d'entrée. Une
/// règle recopiée dix fois finit toujours par diverger neuf fois ; celle-ci ne sera
/// donc écrite qu'ici.
///
/// LES DEUX DROITS SONT DISTINCTS, ET C'EST LE POINT.
///
/// Vendre et se faire payer ne se retirent pas au même moment. Une boutique fermée
/// volontairement ne doit plus rien publier — mais l'argent qu'elle a gagné avant de
/// fermer lui appartient toujours, et le bloquer reviendrait à le confisquer parce
/// qu'elle a rangé sa vitrine.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class SellerRights
{
    private const string Active = "Active";
    private const string Pending = "Pending";
    private const string Suspended = "Suspended";
    private const string Closed = "Closed";
    private const string PendingReactivation = "PendingReactivation";

    /// <summary>
    /// La boutique peut-elle écrire dans son catalogue — produits, variantes, médias,
    /// offres, prix, remises, stock ?
    ///
    /// « Pending » est ACCEPTÉ : le KYB n'est pas encore validé, mais préparer son
    /// catalogue avant l'ouverture est le parcours normal, et rien n'en sort en
    /// vitrine sans la validation d'un administrateur. Refuser ici obligerait tout
    /// nouveau vendeur à attendre son KYB avant de pouvoir seulement saisir un
    /// produit — et déplacerait le goulot d'étranglement au pire endroit.
    ///
    /// « Suspended », « Closed » et « PendingReactivation » sont refusés. Le cas
    /// « Closed » compte double : la fermeture DÉPUBLIE les produits, et sans ce
    /// refus la boutique pouvait les recréer dans la foulée — la sanction était
    /// annulable par sa cible.
    /// </summary>
    public static bool CanSell(string? status)
        => status is Active or Pending;

    /// <summary>
    /// La boutique peut-elle demander le versement de son solde ?
    ///
    /// Seule la SUSPENSION bloque. C'est une mesure conservatoire — fraude
    /// soupçonnée, litige en cours — et laisser sortir les fonds pendant qu'on
    /// instruit viderait la mesure de son sens. L'argent n'est pas confisqué, il
    /// attend la décision.
    ///
    /// Une fermeture volontaire, en revanche, ne retire aucun droit sur ce qui a
    /// été gagné : refuser le retrait à un vendeur qui range sa boutique serait une
    /// confiscation, pas une politique.
    /// </summary>
    public static bool CanWithdraw(string? status)
        => status is Active or Pending or Closed or PendingReactivation;

    /// <summary>
    /// Message à rendre au vendeur refusé. Il NOMME la raison : un « accès refusé »
    /// sans motif envoie le vendeur au support pour une information qu'il pouvait
    /// lire lui-même.
    /// </summary>
    public static string DenialReason(string? status) => status switch
    {
        Suspended => "Votre boutique est suspendue. Contactez le support pour connaître le motif et les suites.",
        Closed => "Votre boutique est fermée. Demandez sa réactivation depuis votre compte pour reprendre la vente.",
        PendingReactivation => "Votre demande de réactivation est en cours d'examen. La vente reprendra dès sa validation.",
        _ => "Votre boutique n'est pas autorisée à effectuer cette action dans son état actuel.",
    };
}
