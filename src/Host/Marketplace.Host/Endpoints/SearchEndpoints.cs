using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Search.Application.Search;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Search : recherche produits sur l'index dénormalisé
/// (alimenté par les events de Catalog, Offers et Reviews). Lecture publique.
/// </summary>
public static class SearchEndpoints
{
    /// <summary>Enregistre la route de recherche.</summary>
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", SearchAsync).WithTags("Search").WithName("SearchProducts").AllowAnonymous();
        return app;
    }

    /// <summary>Recherche produits : texte libre, filtre catégorie, tri (price|rating), pagination.</summary>
    private static async Task<IResult> SearchAsync(
        string? text, Guid? categoryId, string? sort, int? page, int? pageSize, ISender sender, CancellationToken ct)
    {
        var query = new SearchProductsQuery(text, categoryId, sort, page ?? 1, pageSize ?? 20);
        var result = await sender.Send(query, ct);
        return result.Match(results => Results.Ok(results));
    }
}
