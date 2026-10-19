using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

// ALIAS OBLIGATOIRE : .NET 8 a introduit `System.Net.IPNetwork`, qui entre en collision
// avec le `Microsoft.AspNetCore.HttpOverrides.IPNetwork` attendu par
// ForwardedHeadersOptions (CS0104). C'est celui d'ASP.NET qu'il faut : `KnownNetworks`
// est typé avec lui.
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Marketplace.Host.Middleware;

/// <summary>
/// Restaure la VRAIE adresse IP du client derrière Traefik.
///
/// Les hôtes tournent dans le réseau Docker : la connexion que voit Kestrel vient
/// toujours de Traefik, donc <c>RemoteIpAddress</c> valait la même IP pour tout le monde.
/// Le limiteur d'authentification (10 tentatives/minute par IP) partageait alors un seul
/// compteur pour la plateforme entière — aucun isolement contre un attaquant, et un 429
/// pour la onzième personne qui se connectait dans la minute.
///
/// L'en-tête n'est pas usurpable : Traefik AJOUTE la vraie IP en fin de chaîne et
/// <c>ForwardLimit = 1</c> ne lit que la dernière entrée. Ce raisonnement ne tient que si
/// l'en-tête vient d'un proxy de confiance, d'où
/// <see cref="ForwardedHeadersOptions.KnownNetworks"/> restreint aux plages privées du
/// réseau Docker. Un <c>KnownNetworks.Clear()</c> ferait confiance à n'importe qui.
/// </summary>
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
    /// À APPELER EN TOUT PREMIER dans le pipeline HTTP. Tout ce qui suit —
    /// journalisation, authentification, limitation de débit — lit <c>RemoteIpAddress</c> ;
    /// placé après l'un d'eux, il leur donne l'IP du proxy.
    /// </summary>
    public static IApplicationBuilder UseProxyForwardedHeaders(this IApplicationBuilder app)
        => app.UseForwardedHeaders();
}
