using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

// ⚠️ ALIAS OBLIGATOIRE — deux types portent le nom `IPNetwork`.
//
// .NET 8 a introduit `System.Net.IPNetwork`, qui entre en collision avec le
// `Microsoft.AspNetCore.HttpOverrides.IPNetwork` attendu par ForwardedHeadersOptions.
// Sans cet alias, le compilateur ne peut pas trancher (CS0104).
//
// C'est bien celui d'ASP.NET qu'il faut : `KnownNetworks` est typé avec lui sur .NET 8.
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Restaure la VRAIE adresse IP du client derrière Traefik.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CECI, LE RATE LIMITING NE PROTÉGEAIT RIEN — ET OUVRAIT UN DÉNI DE SERVICE.
///
/// Les hôtes tournent dans le réseau Docker, derrière Traefik. La connexion TCP que
/// voit Kestrel vient donc TOUJOURS de Traefik : `RemoteIpAddress` valait la même IP
/// pour tous les utilisateurs du monde.
///
/// Conséquence sur le limiteur d'authentification (10 tentatives/minute, partitionné
/// par IP) :
///
///   • côté SÉCURITÉ — aucun isolement. Un attaquant partageait le même compteur que
///     les utilisateurs légitimes : il ne se distinguait de personne.
///
///   • côté DISPONIBILITÉ — pire encore. Les 10 connexions/minute étaient partagées
///     par la PLATEFORME ENTIÈRE. Onze personnes se connectant dans la même minute, et
///     la onzième recevait un 429. Un déni de service accessible à onze utilisateurs
///     de bonne foi.
///
/// Un garde-fou qui ne garde rien et bloque les innocents : le pire des deux mondes.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>Pourquoi c'est sûr, et pourquoi l'en-tête n'est pas usurpable.</b>
///
/// Un client malveillant peut évidemment envoyer son propre `X-Forwarded-For` bidon.
/// Mais Traefik AJOUTE la véritable IP à la fin de la chaîne. ASP.NET, avec
/// <c>ForwardLimit = 1</c>, ne lit que la DERNIÈRE entrée — celle écrite par le proxy,
/// pas celle fournie par le client. L'usurpation est donc sans effet.
/// </para>
///
/// <para>
/// ⚠️ Ce raisonnement ne tient QUE si l'en-tête vient d'un proxy de confiance. D'où
/// <see cref="ForwardedHeadersOptions.KnownNetworks"/>, restreint aux plages privées du
/// réseau Docker. Un `KnownNetworks.Clear()` — que l'on croise partout sur Internet —
/// ferait confiance à N'IMPORTE QUI : l'attaquant n'aurait plus qu'à changer d'IP
/// déclarée à chaque requête pour contourner toute limite.
/// </para>
public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddProxyForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Un seul proxy entre le client et nous : Traefik. On ne remonte donc qu'un
            // cran. Remonter plus loin reviendrait à faire confiance à des en-têtes que
            // le client a pu écrire lui-même.
            options.ForwardLimit = 1;

            // Les réseaux Docker vivent dans les plages privées RFC 1918. On ne fait
            // confiance qu'à elles.
            options.KnownNetworks.Add(new ProxyNetwork(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownNetworks.Add(new ProxyNetwork(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownNetworks.Add(new ProxyNetwork(IPAddress.Parse("192.168.0.0"), 16));
        });

        return services;
    }

    /// <summary>
    /// ⚠️ À APPELER EN TOUT PREMIER dans le pipeline HTTP.
    ///
    /// Tout ce qui vient ensuite — journalisation, authentification, limitation de
    /// débit — lit <c>RemoteIpAddress</c>. Placer ce middleware après l'un d'eux, c'est
    /// lui donner l'IP du proxy : la correction n'aurait aucun effet, et le bug
    /// paraîtrait corrigé.
    /// </summary>
    public static IApplicationBuilder UseProxyForwardedHeaders(this IApplicationBuilder app)
        => app.UseForwardedHeaders();
}
