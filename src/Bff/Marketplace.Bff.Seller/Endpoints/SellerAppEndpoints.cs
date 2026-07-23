using Microsoft.Extensions.Configuration;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — métadonnées de l'application mobile (/seller/app/*).
///
/// Sert la POLITIQUE DE VERSION consommée par l'app au démarrage pour décider si
/// une mise à jour est obligatoire. Piloté uniquement par la configuration
/// (section « SellerApp ») : forcer une mise à jour ne demande qu'un changement
/// d'appsettings + un redéploiement, sans migration ni base de données.
/// </summary>
public static class SellerAppEndpoints
{
    public static IEndpointRouteBuilder MapSellerAppEndpoints(this IEndpointRouteBuilder app)
    {
        // ANONYME et volontairement : un client trop ancien doit pouvoir apprendre
        // qu'il est périmé AVANT même de tenter une connexion — l'API d'auth qu'il
        // connaît a peut-être déjà changé de contrat. Exiger un jeton ici
        // reviendrait à cacher l'avis de mise à jour derrière un mur qu'il ne peut
        // plus franchir.
        app.MapGet("/seller/app/version", GetVersionPolicy)
            .WithTags("Vendeur · App")
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Politique de version de l'app vendeur.
    ///
    /// Les numéros sont des BUILDS (versionCode Android / CFBundleVersion iOS),
    /// pas des versions marketing : ils sont entiers, strictement croissants, et
    /// comparables de façon fiable — contrairement à « 2.2.1 » qui se compare mal.
    /// </summary>
    private static IResult GetVersionPolicy(IConfiguration config)
    {
        var section = config.GetSection("SellerApp");

        return Results.Ok(new
        {
            // Build MINIMAL accepté. Un build strictement inférieur est bloqué par
            // l'app (écran « mise à jour requise »). 0 = rien n'est bloqué.
            minSupportedBuild = section.GetValue("MinSupportedBuild", 0),

            // Dernier build publié. Purement informatif : permet plus tard une
            // invite « mise à jour disponible » non bloquante.
            latestBuild = section.GetValue("LatestBuild", 0),

            // Où envoyer l'utilisateur pour mettre à jour, par plateforme.
            updateUrlAndroid = section.GetValue<string>("UpdateUrlAndroid") ?? string.Empty,
            updateUrlIos = section.GetValue<string>("UpdateUrlIos") ?? string.Empty,

            // Message facultatif affiché sur l'écran de blocage (ex. « Cette
            // version n'est plus compatible avec le nouveau calcul des frais »).
            message = section.GetValue<string>("Message") ?? string.Empty,
        });
    }
}
