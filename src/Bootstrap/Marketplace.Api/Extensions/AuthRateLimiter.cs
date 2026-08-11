using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Limitation de débit.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ⚠️ CE FICHIER SUPPOSE QUE `UseProxyForwardedHeaders()` A ÉTÉ APPELÉ EN PREMIER.
///
/// Sans lui, `RemoteIpAddress` vaut l'IP de Traefik pour TOUS les utilisateurs : les
/// partitions se confondent en une seule, la protection disparaît, et le quota devient
/// un plafond global — un déni de service que onze utilisateurs de bonne foi suffisent
/// à déclencher. Voir ForwardedHeadersSetup.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>Le CGNAT change tout, et c'est la raison des chiffres ci-dessous.</b>
///
/// Au Bénin, les opérateurs mobiles partagent une même adresse IP publique entre des
/// milliers d'abonnés (Carrier-Grade NAT). Une limite « par IP » n'est donc PAS une
/// limite par personne : c'est une limite par QUARTIER.
///
/// Les 10 tentatives/minute d'origine — un réglage raisonnable en Europe — auraient
/// verrouillé la connexion pour tous les clients d'un opérateur dès qu'une poignée
/// d'entre eux se connectaient en même temps. On aurait diagnostiqué « le serveur est
/// lent », jamais « notre limiteur nous coupe l'accès ».
///
/// D'où deux principes :
///   • dès que l'utilisateur est AUTHENTIFIÉ, on partitionne par IDENTIFIANT DE COMPTE,
///     et non par IP. Le CGNAT cesse alors d'exister pour nous ;
///   • pour les routes anonymes, les seuils par IP sont volontairement LARGES : ils
///     arrêtent une attaque volumétrique, pas un immeuble.
/// </para>
///
/// <para>
/// ⚠️ CE QUE CECI NE FAIT PAS. Un plafond par IP, même bien réglé, ne protège pas un
/// COMPTE précis d'une attaque lente et distribuée (dix mots de passe par heure, depuis
/// mille adresses). La vraie parade est un verrouillage PAR COMPTE après N échecs — à
/// implémenter dans le handler de connexion, avec un compteur Redis. Ce n'est pas fait,
/// et il faut le savoir plutôt que se croire protégé.
/// </para>
public static class AuthRateLimiter
{
    /// <summary>Politique stricte : connexion, inscription, réinitialisation de mot de passe.</summary>
    public const string PolicyName = "auth";

    /// <summary>Politique générale, appliquée à TOUT le reste (catalogue, recherche…).</summary>
    public const string GlobalPolicyName = "global";

    // ── Authentification ────────────────────────────────────────────────────────
    // 30/min et non 10 : derrière un CGNAT, dix personnes se connectant dans la même
    // minute ne sont pas une attaque, c'est une heure de pointe. Un attaquant réel
    // envoie des milliers de requêtes : 30 le freine tout autant que 10, sans punir
    // les utilisateurs légitimes.
    private const int AuthPermitPerWindow = 30;
    private static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    // ── Général ─────────────────────────────────────────────────────────────────
    // Un utilisateur qui navigue peut faire 30 à 60 appels/minute (accueil, fiches,
    // recherche). 300 laisse une marge confortable — et arrête net un robot qui
    // aspirerait le catalogue ou testerait des identifiants de commande au hasard.
    private const int GlobalPermitPerWindow = 300;
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);

    // ── Partenaires de l'API HBA Delivery ───────────────────────────────────────
    //
    // Un partenaire est un SERVEUR, pas un humain : il appelle en rafale au moment
    // où ses commandes tombent, puis se tait. 600/minute laisse passer une pointe
    // de dix commandes par seconde.
    private const int PartnerPermitPerWindow = 600;

    // Plafond par ADRESSE, qui s'ajoute au précédent. Il existe pour une raison
    // précise : un préfixe de clé s'invente. Sans lui, il suffirait de générer des
    // clés bidons bien formées pour s'offrir un seau neuf à chaque requête et
    // contourner entièrement la limitation.
    private const int PartnerIpCeilingPerWindow = 1_200;

    public static IServiceCollection AddAuthRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Politique STRICTE — endpoints d'authentification (anonymes par nature :
            // on ne peut donc partitionner que par IP).
            options.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth:{ClientIp(httpContext)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = AuthPermitPerWindow,
                        Window = AuthWindow,
                        QueueLimit = 0
                    }));

            // Politique GÉNÉRALE — tout le reste.
            //
            // La clé de partition est l'IDENTIFIANT DE COMPTE dès qu'il existe. C'est ce
            // qui neutralise le CGNAT : deux clients du même opérateur ne se volent plus
            // leur quota. On ne retombe sur l'IP que pour les visiteurs anonymes.
            options.AddPolicy(GlobalPolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitPerWindow,
                        Window = GlobalWindow,
                        QueueLimit = 0
                    }));

            // ─────────────────────────────────────────────────────────────────
            // LIMITEUR GLOBAL — DEUX LIMITEURS CHAÎNÉS.
            //
            // Appliqué à TOUS les endpoints qui n'en déclarent pas d'autre. Avant,
            // seuls les groupes `/auth` étaient protégés : la recherche, le
            // catalogue et les fiches produit — tout ce qui est anonyme et coûteux
            // — étaient ouverts.
            //
            // POURQUOI IL EST DÉSORMAIS CHAÎNÉ.
            //
            // Un partenaire de l'API publique n'a pas de compte utilisateur : il
            // tombait donc dans le seau de son ADRESSE. Deux partenaires hébergés
            // chez le même fournisseur se volaient leur quota — exactement le
            // raisonnement CGNAT écrit plus haut pour les utilisateurs mobiles,
            // jamais étendu aux clés d'API.
            //
            // La correction évidente — partitionner par préfixe de clé — ouvre un
            // trou : le préfixe est lu dans un EN-TÊTE, donc il s'invente. Un
            // attaquant générerait une clé bien formée par requête et obtiendrait
            // une partition vierge à chaque fois.
            //
            // D'où le second maillon : un plafond par adresse, actif UNIQUEMENT
            // sur le chemin de l'API partenaire. Une requête doit passer les deux.
            // Un partenaire légitime n'est plus gêné par ses voisins ; un
            // fabricant de préfixes reste enfermé dans le plafond de son IP.
            //
            // ⚠️ Le chaînage n'est possible QUE sur GlobalLimiter : AddPolicy
            // n'accepte qu'un partitionneur simple, pas un PartitionedRateLimiter.
            // ─────────────────────────────────────────────────────────────────
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(

                // Maillon 1 — l'identité : clé d'API, compte, ou adresse.
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: PartitionKey(httpContext),
                        // Le plafond DÉPEND de la partition : un partenaire est un
                        // serveur qui appelle en rafale au moment où ses commandes
                        // tombent, pas un humain qui navigue.
                        factory: key => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = key.StartsWith(PartnerPartitionPrefix, StringComparison.Ordinal)
                                ? PartnerPermitPerWindow
                                : GlobalPermitPerWindow,
                            Window = GlobalWindow,
                            QueueLimit = 0
                        })),

                // Maillon 2 — le plafond par adresse, sur le seul chemin partenaire.
                //
                // GetNoLimiter ailleurs : sans lui, ce second seau s'appliquerait à
                // toute l'application et diviserait par deux le quota déjà réglé
                // plus haut, sans que personne ne comprenne pourquoi.
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    IsPartnerApi(httpContext)
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"partner-ip:{ClientIp(httpContext)}",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = PartnerIpCeilingPerWindow,
                                Window = GlobalWindow,
                                QueueLimit = 0
                            })
                        : RateLimitPartition.GetNoLimiter<string>("hors-api-partenaire")));
        });

        return services;
    }

    /// <summary>Préfixe des partitions partenaires. Sert aussi à moduler leur plafond.</summary>
    private const string PartnerPartitionPrefix = "partner:";

    /// <summary>Chemin de l'API publique partenaires. Voir PartitionKey.</summary>
    private const string PartnerApiPath = "/api/v1/deliveries";

    /// <summary>La requête vise-t-elle l'API publique partenaires ?</summary>
    private static bool IsPartnerApi(HttpContext httpContext)
        => httpContext.Request.Path.StartsWithSegments(PartnerApiPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Clé de partition : la CLÉ D'API sur l'API partenaire, le COMPTE si
    /// l'appelant est authentifié, l'IP sinon.
    ///
    /// C'est la ligne qui rend le limiteur utilisable en Afrique de l'Ouest. Partitionner
    /// un utilisateur connecté par son IP reviendrait à faire payer à chacun la
    /// navigation de tous ses voisins d'opérateur.
    ///
    /// ⚠️ LE PRÉFIXE DE CLÉ N'EST RETENU QUE SUR LE CHEMIN DE L'API PARTENAIRE.
    ///
    /// Il est lu dans un en-tête, donc il s'invente. L'accepter partout
    /// permettrait de s'offrir une partition vierge sur N'IMPORTE QUELLE route —
    /// catalogue, recherche, connexion — en ajoutant un « X-API-Key » bidon.
    /// Restreint au chemin partenaire, il est encadré par le plafond par IP de la
    /// politique chaînée ; ailleurs, on retombe sur le comportement d'origine.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext)
    {
        if (IsPartnerApi(httpContext))
        {
            var presented = httpContext.Request.Headers[PartnerApiKeyFilter.HeaderName].ToString();

            // ExtractPrefix ne touche PAS la base : c'est une vérification de forme
            // sur la chaîne présentée. Un flot de clés malformées est donc écarté
            // ici sans coûter une requête PostgreSQL.
            if (HBA.Deliveries.Domain.Partners.PartnerApiKey.ExtractPrefix(presented) is { } prefix)
            {
                return PartnerPartitionPrefix + prefix;
            }
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? httpContext.User.FindFirstValue("sub");

        return string.IsNullOrWhiteSpace(userId)
            ? $"ip:{ClientIp(httpContext)}"
            : $"user:{userId}";
    }

    /// <summary>
    /// IP du client. Fiable UNIQUEMENT parce que `UseProxyForwardedHeaders()` s'exécute
    /// en amont et a déjà remplacé l'IP de Traefik par celle du vrai client.
    /// </summary>
    private static string ClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
