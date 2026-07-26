using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — catalogue & recherche. La fiche produit est agrégée en un appel
/// (produit + offres/vendeurs + avis résumés), avec dégradation gracieuse.
/// </summary>
public static class MobileCatalogEndpoints
{
    public static IEndpointRouteBuilder MapMobileCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/mobile").WithTags("Mobile · Catalogue");

        mobile.MapGet("/products/{id:guid}", ProductDetailAsync).AllowAnonymous();
        mobile.MapGet("/products/{id:guid}/reviews", ProductReviewsAsync).AllowAnonymous();
        mobile.MapGet("/products/{id:guid}/related", ProductRelatedAsync).AllowAnonymous();
        mobile.MapGet("/search", SearchAsync).AllowAnonymous();
        mobile.MapGet("/search/suggest", SuggestAsync).AllowAnonymous();
        mobile.MapGet("/shop/{sellerId:guid}", ShopAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Fiche produit agrégée : prix/offres (+ nom boutique) + avis résumés (1 appel).</summary>
    private static async Task<IResult> ProductDetailAsync(
        Guid id, ICatalogModuleApi catalog, IOfferModuleApi offers, ISellerModuleApi sellers, ISender sender, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(id, ct);
        if (product is null)
        {
            return Results.NotFound(new { error = "product_not_found" });
        }

        var productOffers = await offers.GetOffersByProductAsync(id, ct);
        var rating = await sender.Send(new GetProductRatingQuery(id), ct);

        // Résout le nom de boutique de chaque vendeur (séquentiel : DbContext scoped).
        var sellerNames = new Dictionary<Guid, string>();
        foreach (var sid in productOffers.Select(o => o.SellerId).Distinct())
        {
            var s = await sellers.GetSellerAsync(sid, ct);
            sellerNames[sid] = s?.ShopName ?? "Vendeur";
        }

        var enrichedOffers = productOffers.Select(o => new
        {
            id = o.Id,
            offerId = o.Id,
            productId = o.ProductId,
            sellerId = o.SellerId,
            sellerName = sellerNames.TryGetValue(o.SellerId, out var n) ? n : "Vendeur",
            sku = o.Sku,
            basePriceAmount = o.BasePriceAmount,
            currency = o.Currency,
            condition = o.Condition,
            status = o.Status,
        });

        return Results.Ok(new
        {
            product,
            offers = enrichedOffers,
            rating = rating.IsSuccess ? rating.Value : null,
        });
    }

    /// <summary>Avis paginés du produit.</summary>
    private static async Task<IResult> ProductReviewsAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsByProductQuery(id), ct)).Match(r => Results.Ok(r));

    /// <summary>Produits similaires / co-achats.</summary>
    private static async Task<IResult> ProductRelatedAsync(Guid id, string? type, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRecommendationsQuery(id, type ?? "Similar"), ct)).Match(r => Results.Ok(r));

    /// <summary>Recherche compacte (vignettes) — ?q=&category=&sort=&page=&size=.</summary>
    private static async Task<IResult> SearchAsync(
        string? q, Guid? category, string? categoryIds, string? sort, int? page, int? size, ISender sender, ICatalogModuleApi catalog, CancellationToken ct)
    {
        // categoryIds = liste CSV d'identifiants (un rayon + son sous-arbre), résolue
        // côté app depuis l'arbre des catégories. Prioritaire sur `category`.
        var ids = ParseGuidCsv(categoryIds);
        var result = await sender.Send(new SearchProductsQuery(q, category, sort, page ?? 1, size ?? 20, CategoryIds: ids), ct);
        if (result.IsFailure)
        {
            return result.Match(r => Results.Ok(r));
        }

        // Enrichit chaque vignette avec l'image principale du produit.
        var enriched = await MobileBff.WithProductImagesAsync(result.Value, catalog, ct);
        return Results.Ok(enriched);
    }

    /// <summary>Parse une liste CSV de GUID (ex. « id1,id2 »). Null/vide → null.</summary>
    private static IReadOnlyList<Guid>? ParseGuidCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var ids = new List<Guid>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.Count > 0 ? ids : null;
    }

    /// <summary>Autocomplétion : top résultats compacts pour ?q=.</summary>
    private static async Task<IResult> SuggestAsync(string? q, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SearchProductsQuery(q, null, null, 1, 8), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value.Items.Select(i => new { i.ProductId, i.Name }))
            : Results.Ok(Array.Empty<object>());
    }

    /// <summary>Vitrine vendeur : profil + note + produits du vendeur (vignettes avec image).</summary>
    private static async Task<IResult> ShopAsync(
        Guid sellerId, ISellerModuleApi sellers, ISender sender, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await sellers.GetSellerAsync(sellerId, ct);
        if (seller is null)
        {
            return Results.NotFound(new { error = "shop_not_found" });
        }

        object products = Array.Empty<object>();
        var result = await sender.Send(new SearchProductsQuery(null, null, null, 1, 50, sellerId), ct);
        if (result.IsSuccess)
        {
            var enriched = await MobileBff.WithProductImagesAsync(result.Value, catalog, ct);
            products = enriched.Items;
        }

        // ⚠️ `.ToPublic()` — SANS LUI, CET ENDPOINT ANONYME FUIT LE RIB ET LES PIÈCES KYB.
        //
        // `seller` est un SellerSummary COMPLET : numéro Mobile Money, taux de commission,
        // URLs des documents d'identité. Le sérialiser tel quel sur une route anonyme
        // permettait de moissonner ces données pour tous les vendeurs. On projette la
        // vitrine publique, qui ne contient que ce qu'un visiteur a le droit de voir.
        return Results.Ok(new { seller = seller.ToPublic(), products });
    }
}
