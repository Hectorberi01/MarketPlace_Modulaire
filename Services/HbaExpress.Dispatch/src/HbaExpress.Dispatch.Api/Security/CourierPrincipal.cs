using System.Security.Claims;

namespace HbaExpress.Dispatch.Api.Security;

/// <summary>
/// Qui appelle, côté application coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JETON PORTE UN COMPTE UTILISATEUR, PAS UN IDENTIFIANT DE COURSIER.
///
/// La marketplace n'émet qu'un seul type de jeton, pour tous ses publics : acheteurs,
/// vendeurs, coursiers. Sa claim « sub » est donc l'identifiant du COMPTE. L'identifiant
/// de coursier, lui, est un second Guid, attribué par le module Delivery — c'est celui
/// qui désigne le portefeuille à créditer, et il ne figure dans aucun jeton.
///
/// Les routes traduisent donc l'un en l'autre (voir CourierService.ResolveByUserAsync).
/// Prendre la claim pour un identifiant de coursier — ce qui était fait ici — donnait un
/// service qui offre des courses à un coursier enregistré et répond 404 à ce même
/// coursier quand il essaie de les prendre.
///
/// L'IDENTIFIANT VIENT DU JETON, JAMAIS DU CORPS DE LA REQUÊTE. Laisser l'appelant
/// annoncer le sien permettrait à n'importe quel porteur d'un jeton valide de se mettre
/// en ligne à la place d'un autre, ou d'accepter ses courses. Le jeton est signé ; le
/// corps ne l'est pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierPrincipal
{
    /// <summary>
    /// Identifiant du COMPTE appelant, ou <c>null</c> si le jeton n'en porte pas
    /// d'exploitable — auquel cas la route répond 401, sans planter.
    /// </summary>
    public static Guid? ResolveUserId(ClaimsPrincipal principal)
    {
        // Le gestionnaire JWT remappe « sub » vers NameIdentifier ; on lit les deux, la
        // configuration de ce remappage pouvant changer sans qu'on s'en aperçoive.
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
