using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Recommendations.Contracts;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Search.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — accueil & découverte (/mobile/home, /categories, /recommendations).
/// L'écran d'accueil agrège en un seul appel (mises en avant + reco perso).
/// </summary>
public static class MobileDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapMobileDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/mobile").WithTags("Mobile · Découverte");

        mobile.MapGet("/home", HomeAsync).AllowAnonymous();
        mobile.MapGet("/recommendations", RecommendationsAsync).RequireAuthorization();

        // Catégories actives (chips d'accueil & recherche).
        mobile.MapGet("/categories", CategoriesAsync).AllowAnonymous();
        mobile.MapGet("/categories/{slug}", CategoryBySlugAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Liste plate des catégories actives (id, nom, slug) pour les filtres mobiles.</summary>
    private static async Task<IResult> CategoriesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListCategoriesQuery(), ct);
        if (result.IsFailure)
        {
            return Results.Ok(Array.Empty<object>());
        }

        var active = result.Value
            .Where(c => string.Equals(c.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(c => new { id = c.Id, name = c.Name, slug = c.Slug, parentId = c.ParentId, imageUrl = c.ImageUrl });

        return Results.Ok(active);
    }

    /// <summary>Catégorie par slug (résolue depuis la liste).</summary>
    private static async Task<IResult> CategoryBySlugAsync(string slug, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListCategoriesQuery(), ct);
        if (result.IsFailure)
        {
            return Results.NotFound(new { error = "category_not_found" });
        }

        var match = result.Value.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? Results.NotFound(new { error = "category_not_found" })
            : Results.Ok(new { id = match.Id, name = match.Name, slug = match.Slug, parentId = match.ParentId, imageUrl = match.ImageUrl });
    }

    /// <summary>Écran d'accueil agrégé : mises en avant + recommandations personnalisées (1 appel).</summary>
    private static async Task<IResult> HomeAsync(ClaimsPrincipal user, ISender sender, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var featured = await sender.Send(new SearchProductsQuery(null, null, null, 1, 10), ct);

        // Enrichit les mises en avant avec l'image principale de chaque produit.
        var featuredPage = featured.IsSuccess
            ? await MobileBff.WithProductImagesAsync(featured.Value, catalog, ct)
            : null;

        RecommendationSummary? personalized = null;
        if (MobileBff.CurrentUserId(user) is { } userId)
        {
            // Dégradation gracieuse : si la reco échoue, l'accueil reste servi.
            var reco = await sender.Send(new GetUserRecommendationsQuery(userId), ct);
            if (reco.IsSuccess)
            {
                personalized = reco.Value;
            }
        }

        return Results.Ok(new MobileHome(
            Featured: featuredPage,
            Recommendations: personalized));
    }

    /// <summary>Recommandations personnalisées de l'utilisateur connecté.</summary>
    private static async Task<IResult> RecommendationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new GetUserRecommendationsQuery(userId), ct)).Match(r => Results.Ok(r));
    }

    public sealed record MobileHome(SearchResultPage? Featured, RecommendationSummary? Recommendations);
}
