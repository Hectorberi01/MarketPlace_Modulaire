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

    /// <summary>Fiche produit agrégée : prix/offres + avis résumés (1 appel).</summary>
    private static async Task<IResult> ProductDetailAsync(
        Guid id, ICatalogModuleApi catalog, IOfferModuleApi offers, ISender sender, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(id, ct);
        if (product is null)
        {
            return Results.NotFound(new { error = "product_not_found" });
        }

        var productOffers = await offers.GetOffersByProductAsync(id, ct);
        var rating = await sender.Send(new GetProductRatingQuery(id), ct);

        return Results.Ok(new MobileProductDetail(
            product, productOffers, rating.IsSuccess ? rating.Value : null));
    }

    /// <summary>Avis paginés du produit.</summary>
    private static async Task<IResult> ProductReviewsAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsByProductQuery(id), ct)).Match(r => Results.Ok(r));

    /// <summary>Produits similaires / co-achats.</summary>
    private static async Task<IResult> ProductRelatedAsync(Guid id, string? type, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRecommendationsQuery(id, type ?? "Similar"), ct)).Match(r => Results.Ok(r));

    /// <summary>Recherche compacte (vignettes) — ?q=&category=&sort=&page=&size=.</summary>
    private static async Task<IResult> SearchAsync(
        string? q, Guid? category, string? sort, int? page, int? size, ISender sender, CancellationToken ct)
        => (await sender.Send(new SearchProductsQuery(q, category, sort, page ?? 1, size ?? 20), ct)).Match(r => Results.Ok(r));

    /// <summary>Autocomplétion : top résultats compacts pour ?q=.</summary>
    private static async Task<IResult> SuggestAsync(string? q, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SearchProductsQuery(q, null, null, 1, 8), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value.Items.Select(i => new { i.ProductId, i.Name }))
            : Results.Ok(Array.Empty<object>());
    }

    /// <summary>Vitrine vendeur : profil + note. (Liste produits du vendeur : query Search par vendeur à ajouter.)</summary>
    private static async Task<IResult> ShopAsync(Guid sellerId, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await sellers.GetSellerAsync(sellerId, ct);
        return seller is null
            ? Results.NotFound(new { error = "shop_not_found" })
            : Results.Ok(new MobileShop(seller, Array.Empty<object>()));
    }

    public sealed record MobileProductDetail(ProductSummary Product, IReadOnlyList<OfferSummary> Offers, ProductRatingSummary? Rating);
    public sealed record MobileShop(SellerSummary Seller, IReadOnlyList<object> Products);
}
