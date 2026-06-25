using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
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

        // Arbre des catégories : nécessite une query de liste/arbre côté Catalog
        // (aujourd'hui seul GetCategory(id) existe).
        mobile.MapGet("/categories", () => MobileBff.NotImplemented("catalog.list_categories")).AllowAnonymous();
        mobile.MapGet("/categories/{slug}", (string slug) => MobileBff.NotImplemented("catalog.category_by_slug")).AllowAnonymous();

        return app;
    }

    /// <summary>Écran d'accueil agrégé : mises en avant + recommandations personnalisées (1 appel).</summary>
    private static async Task<IResult> HomeAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var featured = await sender.Send(new SearchProductsQuery(null, null, null, 1, 10), ct);

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
            Featured: featured.IsSuccess ? featured.Value : null,
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
