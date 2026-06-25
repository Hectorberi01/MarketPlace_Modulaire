using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>
/// BFF Web — surfaces propres au web (enrichi). Comparateur multi-produits
/// (jusqu'à 4) agrégé en un appel, et emplacements SEO/contenu marketing
/// (renvoyés en 501 tant que le module Content n'est pas exposé).
/// </summary>
public static class WebStorefrontEndpoints
{
    private const int MaxCompareItems = 4;

    public static IEndpointRouteBuilder MapWebStorefrontEndpoints(this IEndpointRouteBuilder app)
    {
        var web = app.MapGroup("/web").WithTags("Web · Vitrine");

        web.MapGet("/compare", CompareAsync).AllowAnonymous();

        // Contenu marketing / pages éditoriales (landing, CMS) et flux produits SEO
        // (sitemap, Google Merchant) : nécessitent un module Content / un export
        // catalogue non encore exposés.
        web.MapGet("/content/{slug}", (string slug) => WebBff.NotImplemented("content.pages")).AllowAnonymous();
        web.MapGet("/feed/products", () => WebBff.NotImplemented("catalog.product_feed")).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Comparateur : agrège produit + offres + note pour une liste d'identifiants
    /// (?ids=guid,guid,…), dans l'ordre demandé, avec dégradation gracieuse (les
    /// identifiants introuvables sont ignorés).
    /// </summary>
    private static async Task<IResult> CompareAsync(
        string? ids, ICatalogModuleApi catalog, IOfferModuleApi offers, ISender sender, CancellationToken ct)
    {
        var productIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .Distinct()
            .Take(MaxCompareItems)
            .ToList();

        if (productIds.Count == 0)
        {
            return Results.BadRequest(new { error = "missing_ids", hint = "?ids=guid,guid" });
        }

        var items = new List<WebCompareItem>(productIds.Count);
        foreach (var id in productIds)
        {
            var product = await catalog.GetProductAsync(id, ct);
            if (product is null)
            {
                continue; // introuvable : ignoré pour ne pas casser la comparaison.
            }

            var productOffers = await offers.GetOffersByProductAsync(id, ct);
            var rating = await sender.Send(new GetProductRatingQuery(id), ct);
            items.Add(new WebCompareItem(product, productOffers, rating.IsSuccess ? rating.Value : null));
        }

        return Results.Ok(new WebCompareResult(items));
    }

    public sealed record WebCompareItem(ProductSummary Product, IReadOnlyList<OfferSummary> Offers, ProductRatingSummary? Rating);
    public sealed record WebCompareResult(IReadOnlyList<WebCompareItem> Items);
}
