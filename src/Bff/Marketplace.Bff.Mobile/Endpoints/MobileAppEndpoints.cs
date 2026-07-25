using Microsoft.Extensions.Configuration;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — métadonnées de l'application acheteur (/mobile/app/*).
///
/// Sert la POLITIQUE DE VERSION consommée par l'app au démarrage pour décider si
/// une mise à jour est obligatoire. Piloté par la configuration (section
/// « MobileApp ») : forcer une mise à jour ne demande qu'un changement
/// d'appsettings + un redéploiement, sans migration ni base de données.
/// </summary>
public static class MobileAppEndpoints
{
    public static IEndpointRouteBuilder MapMobileAppEndpoints(this IEndpointRouteBuilder app)
    {
        // ANONYME et volontairement : un client trop ancien doit pouvoir apprendre
        // qu'il est périmé AVANT même de tenter une connexion.
        app.MapGet("/mobile/app/version", GetVersionPolicy)
            .WithTags("Mobile · App")
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Politique de version de l'app acheteur. Les numéros sont des BUILDS
    /// (versionCode Android / CFBundleVersion iOS) : entiers strictement
    /// croissants, comparables de façon fiable.
    /// </summary>
    private static IResult GetVersionPolicy(IConfiguration config)
    {
        var section = config.GetSection("MobileApp");

        return Results.Ok(new
        {
            // Build MINIMAL accepté. Un build strictement inférieur est bloqué par
            // l'app (écran « mise à jour requise »). 0 = rien n'est bloqué.
            minSupportedBuild = section.GetValue("MinSupportedBuild", 0),

            // Dernier build publié (informatif).
            latestBuild = section.GetValue("LatestBuild", 0),

            updateUrlAndroid = section.GetValue<string>("UpdateUrlAndroid") ?? string.Empty,
            updateUrlIos = section.GetValue<string>("UpdateUrlIos") ?? string.Empty,

            message = section.GetValue<string>("Message") ?? string.Empty,
        });
    }
}
