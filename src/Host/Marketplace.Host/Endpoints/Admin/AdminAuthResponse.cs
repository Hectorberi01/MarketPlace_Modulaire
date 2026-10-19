using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// Ce que la console d'administration reçoit après un login ou un refresh.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// REGARDEZ CE QUI N'EST PAS LÀ : LE JETON DE RAFRAÎCHISSEMENT.
///
/// L'API renvoyait auparavant l'<c>AuthTokens</c> complet — jeton d'accès ET jeton de
/// rafraîchissement. La console Blazor rangeait ce dernier dans `localStorage`, où
/// n'importe quel script peut le lire. Une XSS suffisait à l'emporter, et l'attaquant
/// disposait de 30 jours d'accès administrateur depuis sa propre machine.
///
/// Ce record est la barrière : le jeton de rafraîchissement ne peut plus être sérialisé
/// vers le navigateur, puisqu'il n'a plus de champ où loger. Il part exclusivement en
/// cookie HttpOnly (voir RefreshTokenCookie).
///
/// N'AJOUTEZ PAS de propriété RefreshToken ici. Ce serait annuler tout le correctif,
/// et le faire silencieusement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="AccessToken">JWT court (15 min), gardé EN MÉMOIRE par la console.</param>
/// <param name="AccessTokenExpiresOnUtc">Permet à la console de renouveler avant expiration.</param>
public sealed record AdminAuthResponse(string AccessToken, DateTime AccessTokenExpiresOnUtc)
{
    public static AdminAuthResponse From(AuthTokens tokens)
        => new(tokens.AccessToken, tokens.AccessTokenExpiresOnUtc);
}

/// <summary>
/// Attributs du cookie de rafraîchissement. Section de config : « Auth:RefreshCookie ».
/// </summary>
public sealed class AdminAuthCookieOptions
{
    /// <summary>
    /// Politique SameSite du cookie. Défaut : <c>Strict</c>.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// STRICT SUFFIT — À CONDITION QUE LA CONSOLE ET L'API SOIENT SUR LE MÊME SITE.
    ///
    /// En production, la console est servie sur `admin.${ROOT_DOMAIN}` et l'API sur le
    /// même domaine racine : c'est le MÊME SITE au sens SameSite (même eTLD+1), donc le
    /// cookie part normalement, et aucune requête venue d'un autre site ne l'emporte.
    ///
    /// SI VOUS HÉBERGEZ UN JOUR LA CONSOLE SUR UN AUTRE DOMAINE RACINE (Netlify,
    /// Vercel, un domaine de secours…), `Strict` empêchera le cookie de partir : les
    /// administrateurs seront déconnectés à chaque rechargement de page, sans erreur
    /// visible. Il faudra alors passer à `None` — ET c'est précisément pour ce cas que
    /// la garde anti-CSRF par en-tête personnalisé existe : elle protège la route même
    /// sans l'aide de SameSite.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;
}
