using System.Security.Claims;
using Marketplace.Modules.Catalog.Contracts;
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
        SearchResultPage page, ICatalogModuleApi catalog, CancellationToken ct)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        // Séquentiel : le DbContext (scoped) du module Catalog n'est pas
        // thread-safe ; on ne lance pas les requêtes en parallèle.
        var items = new List<SearchResultItem>(page.Items.Count);
        foreach (var item in page.Items)
        {
            if (!string.IsNullOrEmpty(item.ImageUrl))
            {
                items.Add(item);
                continue;
            }

            var product = await catalog.GetProductAsync(item.ProductId, ct);
            if (product is null || product.Media.Count == 0)
            {
                items.Add(item);
                continue;
            }

            var primary = product.Media.FirstOrDefault(m => m.IsPrimary) ?? product.Media[0];
            items.Add(item with { ImageUrl = primary.Url });
        }

        return page with { Items = items };
    }
}
