using Marketplace.BuildingBlocks.Domain.Geography;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// Référentiel géographique servi à la console et à l'application vendeur.
///
/// Même charge utile que le BFF Mobile — <see cref="BeninGeography.Reference"/> — et
/// c'est volontaire : les deux surfaces doivent proposer exactement les mêmes communes
/// que celles que le serveur accepte, sinon un vendeur choisit une valeur qui sera
/// refusée à l'enregistrement sans qu'il comprenne pourquoi.
///
/// ANONYME : la donnée est publique, et l'écran d'inscription vendeur en a besoin
/// AVANT qu'un jeton existe.
/// </summary>
public static class SellerGeographyEndpoints
{
    public static IEndpointRouteBuilder MapSellerGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var geo = app.MapGroup("/api/sellers/geo").WithTags("Vendeur · Géographie").AllowAnonymous();

        geo.MapGet("/communes", ListCommunes)
            .WithName("SellerListCommunes")
            .WithSummary("Les 77 communes du Bénin, avec leur département.");

        return app;
    }

    private static IResult ListCommunes(HttpContext http)
    {
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(BeninGeography.Reference);
    }
}
