using Marketplace.Modules.Identity.Application.Models;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// Le jeton de rafraîchissement de la console d'administration, transporté par un cookie
/// <b>HttpOnly</b> — donc invisible pour JavaScript.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI : LE JETON VIVAIT DANS LE localStorage, ET IL VAUT 30 JOURS D'ADMIN.
///
/// La console est une application Blazor WebAssembly. Elle stockait le refresh token
/// dans `localStorage` sous la clé « mp_refresh ». Or `localStorage` est lisible par
/// N'IMPORTE QUEL script s'exécutant sur la page.
///
/// Une seule faille XSS — une dépendance npm compromise, un champ mal échappé — et
/// l'attaquant exécute `localStorage.getItem('mp_refresh')`, envoie le jeton chez lui,
/// et dispose alors d'un accès ADMINISTRATEUR COMPLET pendant 30 jours, DEPUIS SA PROPRE
/// MACHINE. Fermer l'onglet n'y change rien. Changer d'ordinateur non plus.
///
/// En cookie HttpOnly, ce script ne peut plus LIRE le jeton. Il ne peut donc plus
/// l'emporter.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>CE QUE CE CORRECTIF NE FAIT PAS — ET IL FAUT LE DIRE.</b> HttpOnly n'immunise pas
/// contre le XSS. Un script injecté peut toujours appeler <c>/admin/auth/refresh</c> depuis
/// le navigateur de la victime : le cookie part tout seul, et le script récupère un jeton
/// d'accès. Ce qu'il ne peut plus faire, c'est <b>EXFILTRER</b> une clé de longue durée.
/// L'attaque devient confinée à la session ouverte de la victime, au lieu de survivre un
/// mois sur une machine étrangère. La différence n'est pas cosmétique : elle transforme une
/// compromission durable en une compromission éphémère. Mais elle ne dispense pas de
/// corriger les XSS.
/// </para>
/// </summary>
public static class RefreshTokenCookie
{
    /// <summary>
    /// Préfixe <c>__Secure-</c> : le navigateur REFUSE ce cookie s'il n'est pas posé
    /// depuis HTTPS avec l'attribut Secure. C'est une garantie appliquée par le client, que
    /// même une erreur de configuration serveur ne peut pas contourner.
    /// </summary>
    public const string Name = "__Secure-admin_rt";

    /// <summary>
    /// Le cookie n'est envoyé QUE sur les routes d'authentification.
    ///
    /// Sans ce Path, il accompagnerait chacun des ~200 appels de la console — et se
    /// retrouverait dans les logs d'accès, les traces, les proxies. Un secret qu'on
    /// transmet 200 fois par minute finit toujours par se retrouver quelque part.
    /// </summary>
    public const string Path = "/api/admin/auth";

    /// <summary>
    /// En-tête anti-CSRF. Sa présence est EXIGÉE sur /admin/auth/refresh.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS LUI, LE COOKIE RECRÉERAIT UNE FAILLE EN EN FERMANT UNE AUTRE.
    ///
    /// Un cookie part TOUT SEUL avec la requête. Un site malveillant pourrait donc
    /// faire soumettre au navigateur de l'admin un formulaire vers /admin/auth/refresh :
    /// le cookie suivrait, et l'attaquant obtiendrait un jeton d'accès. C'est le CSRF.
    ///
    /// La parade : exiger un en-tête PERSONNALISÉ. Un formulaire HTML ne peut pas en
    /// poser ; seul un `fetch`/XHR le peut, et un `fetch` cross-origin portant un
    /// en-tête personnalisé déclenche un preflight CORS — que notre politique refuse
    /// pour toute origine non déclarée. L'attaque est donc bloquée par le navigateur
    /// lui-même, avant même d'atteindre le serveur.
    ///
    /// Cette protection tient MÊME si le cookie devait passer en SameSite=None (console
    /// hébergée sur un autre domaine racine). C'est pourquoi elle existe : SameSite seul
    /// serait une hypothèse sur le déploiement, pas une garantie.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public const string AntiCsrfHeader = "X-Admin-Refresh";

    /// <summary>Pose (ou remplace) le cookie de rafraîchissement.</summary>
    public static void Issue(HttpContext httpContext, AuthTokens tokens, SameSiteMode sameSite)
    {
        httpContext.Response.Cookies.Append(Name, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,   // ← LE point : JavaScript ne peut pas le lire.
            Secure = true,     // HTTPS uniquement (localhost reste autorisé par les navigateurs).
            SameSite = sameSite,
            Path = Path,

            // Expire avec le jeton lui-même : pas de cookie fantôme qui survivrait au
            // droit qu'il représente.
            Expires = new DateTimeOffset(tokens.RefreshTokenExpiresOnUtc, TimeSpan.Zero),
        });
    }

    /// <summary>Lit le jeton de rafraîchissement, ou null s'il n'y a pas de cookie.</summary>
    public static string? Read(HttpContext httpContext)
        => httpContext.Request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrWhiteSpace(token)
            ? token
            : null;

    /// <summary>
    /// Supprime le cookie. Les attributs (Path, Secure, SameSite) DOIVENT correspondre à
    /// ceux de la pose — sinon le navigateur considère qu'il s'agit d'un autre cookie et
    /// garde l'ancien. Une déconnexion qui ne déconnecte pas est pire que pas de
    /// déconnexion du tout : elle rassure à tort.
    /// </summary>
    public static void Clear(HttpContext httpContext, SameSiteMode sameSite)
    {
        httpContext.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = sameSite,
            Path = Path,
        });
    }
}
