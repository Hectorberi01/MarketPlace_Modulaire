using Marketplace.BuildingBlocks.Domain.Geography;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// Référentiel géographique servi à la console d'administration.
///
/// Même charge utile que les BFF Mobile et Vendeur — <see cref="BeninGeography.Reference"/>.
/// Les trois consoles doivent proposer exactement les communes que le serveur accepte,
/// sinon un administrateur choisit une valeur refusée à l'enregistrement sans comprendre
/// pourquoi.
///
/// ANONYME, comme ses homologues : c'est une donnée publique, et le cache de 24 h évite
/// de la redemander à chaque ouverture d'écran.
/// </summary>
public static class AdminGeographyEndpoints
{
    public static IEndpointRouteBuilder MapAdminGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var geo = app.MapGroup("/api/admin/geo").WithTags("Admin · Géographie").AllowAnonymous();

        geo.MapGet("/communes", ListCommunes)
            .WithName("AdminListCommunes")
            .WithSummary("Les 77 communes du Bénin, avec leur département.");

        return app;
    }

    private static IResult ListCommunes(HttpContext http)
    {
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(BeninGeography.Reference);
    }
}
