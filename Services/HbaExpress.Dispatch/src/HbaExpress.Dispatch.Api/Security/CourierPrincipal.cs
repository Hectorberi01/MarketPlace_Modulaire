using System.Security.Claims;
using HbaExpress.Dispatch.Domain.Couriers;

namespace HbaExpress.Dispatch.Api.Security;

/// <summary>
/// Qui appelle, côté application coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'IDENTIFIANT VIENT DU JETON, JAMAIS DU CORPS DE LA REQUÊTE.
///
/// C'est la seule règle qui compte ici. Laisser l'appelant annoncer son propre
/// identifiant de coursier — dans le corps, dans l'URL — permettrait à n'importe quel
/// porteur d'un jeton valide de se mettre en ligne à la place d'un autre, d'accepter ses
/// courses, ou de déplacer sa position. Le jeton est signé ; le corps ne l'est pas.
///
/// L'identifiant de coursier est CELUI DE L'UTILISATEUR de la marketplace : le module
/// Delivery du monolithe et ce service désignent la même personne par la même clé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierPrincipal
{
    /// <summary>
    /// Identifiant du coursier appelant, ou <c>null</c> si le jeton n'en porte pas
    /// d'exploitable — auquel cas la route doit répondre 401, pas planter.
    /// </summary>
    public static CourierId? Resolve(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? new CourierId(id) : null;
    }
}
