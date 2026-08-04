using Marketplace.BuildingBlocks.Domain.Geography;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// RÉFÉRENTIEL GÉOGRAPHIQUE — LES 77 COMMUNES, SERVIES AUX APPLICATIONS.
///
/// POURQUOI UN ENDPOINT PLUTÔT QU'UNE LISTE EMBARQUÉE DANS CHAQUE APPLICATION
///
/// Quatre surfaces ont besoin de cette liste : l'app acheteur, l'app vendeur et les deux
/// consoles. Dupliquée quatre fois, elle diverge à la première correction d'orthographe —
/// et une commune que le serveur refuse mais que l'application propose produit un message
/// d'erreur incompréhensible pour l'utilisateur.
///
/// Servie ici, le serveur reste l'unique autorité : ce qu'il liste est exactement ce
/// qu'il accepte.
///
/// ANONYME ET FORTEMENT CACHÉE. C'est une donnée publique et quasi immuable — elle n'a
/// pas bougé depuis la réforme de 1999. Exiger un jeton empêcherait de préremplir un
/// formulaire d'inscription, et interroger le serveur à chaque ouverture d'écran serait
/// du gaspillage. Une journée de cache, et l'application peut la conserver hors ligne.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MobileGeographyEndpoints
{
    public static IEndpointRouteBuilder MapMobileGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var geo = app.MapGroup("/mobile/geo").WithTags("Mobile · Géographie").AllowAnonymous();

        geo.MapGet("/communes", ListCommunes)
            .WithName("MobileListCommunes")
            .WithSummary("Les 77 communes du Bénin, avec leur département.");

        return app;
    }

    private static IResult ListCommunes(HttpContext http)
    {
        // 24 h de cache navigateur/CDN. `immutable` serait excessif : le découpage
        // administratif peut changer, très rarement, mais il peut changer.
        http.Response.Headers.CacheControl = "public, max-age=86400";

        return Results.Ok(BeninGeography.Reference);
    }
}
