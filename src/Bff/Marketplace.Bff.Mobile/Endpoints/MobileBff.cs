using System.Security.Claims;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Search.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>Utilitaires partagés des endpoints /mobile (identité issue du jeton).</summary>
internal static class MobileBff
{
    /// <summary>Identifiant de l'utilisateur courant, extrait du JWT (jamais du client).</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Réponse 501 normalisée pour un endpoint dont le module sous-jacent manque encore.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);

    /// <summary>
    /// Enrichit une page de résultats de recherche avec l'URL de l'image
    /// principale de chaque produit (lue du module Catalog). Évite de dépendre
    /// de l'image stockée dans le read model Search (qui peut être absente).
    /// </summary>
    public static async Task<SearchResultPage> WithProductImagesAsync(
        SearchResultPage page, ICatalogModuleApi catalog, IOfferModuleApi offers, CancellationToken ct)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        // Séquentiel : le DbContext (scoped) des modules Catalog/Offers n'est pas
        // thread-safe ; on ne lance pas les requêtes en parallèle.
        var items = new List<SearchResultItem>(page.Items.Count);
        foreach (var item in page.Items)
        {
            var enriched = item;

            // Image principale (si absente du read model Search).
            if (string.IsNullOrEmpty(enriched.ImageUrl))
            {
                var product = await catalog.GetProductAsync(item.ProductId, ct);
                var primary = product?.Media.FirstOrDefault(m => m.IsPrimary) ?? product?.Media.FirstOrDefault();
                if (primary is not null)
                {
                    enriched = enriched with { ImageUrl = primary.Url };
                }
            }

            // Promotion : on regarde l'offre ACTIVE la moins chère du produit — c'est
            // elle qui fixe le prix affiché sur la carte. Si elle a un prix d'origine
            // (CompareAt) supérieur au prix courant, on remonte le prix barré + la fin
            // de remise pour que la carte affiche « −X% ».
            var productOffers = await offers.GetOffersByProductAsync(item.ProductId, ct);
            var cheapest = productOffers
                .Where(o => string.Equals(o.Status, "Active", StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.BasePriceAmount)
                .FirstOrDefault();
            if (cheapest?.CompareAtAmount is { } compareAt && compareAt > cheapest.BasePriceAmount)
            {
                enriched = enriched with
                {
                    CompareAtPrice = compareAt,
                    DiscountEndsOnUtc = cheapest.DiscountEndsOnUtc,
                };
            }

            items.Add(enriched);
        }

        return page with { Items = items };
    }
}
