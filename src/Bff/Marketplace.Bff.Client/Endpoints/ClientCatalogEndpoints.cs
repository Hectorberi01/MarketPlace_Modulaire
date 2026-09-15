using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Client — catalogue & recherche. La fiche produit est agrégée en un appel
/// (produit + offres/vendeurs + avis résumés), avec dégradation gracieuse.
/// </summary>
public static class ClientCatalogEndpoints
{
    public static IEndpointRouteBuilder MapClientCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/client").WithTags("Client · Catalogue");

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
        Guid id, ICatalogModuleApi catalog, IOfferModuleApi offers, ISellerModuleApi sellers,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
    {
        // LECTURE PUBLIQUE : nul si le produit n'est pas en vente. Cette route est
        // anonyme, et elle servait jusqu'ici les brouillons jamais validés comme les
        // produits REJETÉS par un modérateur — le rejet écrivant « Archived ».
        var product = await catalog.GetPubliclyVisibleProductAsync(id, ct);
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

        // ═══════════════════════════════════════════════════════════════════════════
        // LA DISPONIBILITÉ, ANNONCÉE AVANT LE PANIER ET NON APRÈS.
        //
        // Rien n'indiquait qu'un article était en rupture : l'acheteur le découvrait au
        // moment d'ajouter au panier, c'est-à-dire APRÈS avoir décidé d'acheter. C'est
        // la pire place pour une mauvaise nouvelle.
        //
        // ON N'EXPOSE PAS LE CHIFFRE EXACT, ET C'EST DÉLIBÉRÉ. Cette route est ANONYME :
        // publier « 47 en stock » livre à quiconque l'état des stocks de chaque vendeur,
        // renseigne la concurrence et permet de suivre les ventes par différence entre
        // deux relevés. On publie donc un signal à trois états — disponible, bientôt
        // épuisé, indisponible — et le compte exact UNIQUEMENT sous le seuil, là où il
        // sert l'acheteur (« plus que 2 ») plutôt qu'un observateur.
        //
        // SÉQUENTIEL, comme les noms de boutique juste au-dessus : les DbContext sont
        // « scoped » et ne supportent pas les accès concurrents. Une offre ne portant
        // qu'un SKU, on interroge une fois par SKU DISTINCT — plusieurs offres du même
        // article ne déclenchent pas plusieurs requêtes.
        // ═══════════════════════════════════════════════════════════════════════════
        const int seuilStockFaible = 5;
        var disponibilites = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sku in productOffers.Select(o => o.Sku).Distinct(StringComparer.Ordinal))
        {
            try
            {
                disponibilites[sku] = (await inventory.GetAvailabilityAsync(sku, ct)).TotalAvailable;
            }
            catch (Exception)
            {
                // Le stock est un ORNEMENT de la fiche : s'il est indisponible, la fiche
                // doit rester consultable et l'article restera achetable — c'est le Saga
                // de commande qui refusera, avec son propre message. Faire échouer la
                // page entière parce qu'un module annexe ne répond pas serait troquer
                // une information manquante contre une vente perdue.
            }
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
            // Promotion vendeur : prix d'origine barré + fin de remise (null si aucune).
            compareAtAmount = o.CompareAtAmount,
            discountEndsOnUtc = o.DiscountEndsOnUtc,
            // `null` = stock inconnu (module injoignable) : la vitrine n'affiche alors
            // RIEN plutôt qu'un « indisponible » qu'elle ne peut pas garantir.
            inStock = disponibilites.TryGetValue(o.Sku, out var dispo) ? dispo > 0 : (bool?)null,
            lowStock = disponibilites.TryGetValue(o.Sku, out var d2) && d2 > 0 && d2 <= seuilStockFaible,
            // Le compte n'est envoyé QUE sous le seuil. Voir l'encadré ci-dessus.
            availableQuantity = disponibilites.TryGetValue(o.Sku, out var d3) && d3 > 0 && d3 <= seuilStockFaible
                ? d3
                : (int?)null,
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
        string? q, Guid? category, string? categoryIds, string? sort, int? page, int? size, ISender sender, ICatalogModuleApi catalog, IOfferModuleApi offers, CancellationToken ct)
    {
        // categoryIds = liste CSV d'identifiants (un rayon + son sous-arbre), résolue
        // côté app depuis l'arbre des catégories. Prioritaire sur `category`.
        var ids = ParseGuidCsv(categoryIds);
        var result = await sender.Send(new SearchProductsQuery(q, category, sort, page ?? 1, size ?? 20, CategoryIds: ids), ct);
        if (result.IsFailure)
        {
            return result.Match(r => Results.Ok(r));
        }

        // Enrichit chaque vignette avec l'image principale + l'éventuelle promotion.
        var enriched = await ClientBff.WithProductImagesAsync(result.Value, catalog, offers, ct);
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
        Guid sellerId, ISellerModuleApi sellers, ISender sender, ICatalogModuleApi catalog,
        IOfferModuleApi offers, IReviewsModuleApi reviews, IOrderingModuleApi ordering, CancellationToken ct)
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
            var enriched = await ClientBff.WithProductImagesAsync(result.Value, catalog, offers, ct);
            products = enriched.Items;
        }

        // NOTE & VENTES calculées EN DIRECT depuis la source (avis publiés / commandes
        // encaissées), et non lues sur l'entité vendeur. Deux raisons :
        //   • pas de dépendance à un événement : les avis et ventes DÉJÀ existants
        //     s'affichent immédiatement, sans backfill ;
        //   • toujours exact : la vitrine reflète l'état réel à l'instant T.
        // Le compteur persistant du vendeur reste alimenté par les handlers (pour son
        // tableau de bord), mais la vitrine ne s'y fie pas.
        var rating = await reviews.GetSellerRatingAsync(sellerId, ct);
        var salesCount = await ordering.GetSellerSalesCountAsync(sellerId, ct);

        // `.ToPublic()` — SANS LUI, CET ENDPOINT ANONYME FUIT LE RIB ET LES PIÈCES KYB.
        //
        // `seller` est un SellerSummary COMPLET : numéro Mobile Money, taux de commission,
        // URLs des documents d'identité. Le sérialiser tel quel sur une route anonyme
        // permettait de moissonner ces données pour tous les vendeurs. On projette la
        // vitrine publique, qui ne contient que ce qu'un visiteur a le droit de voir.
        var vitrine = seller.ToPublic() with { Rating = (decimal)rating.Average, SalesCount = salesCount };
        return Results.Ok(new { seller = vitrine, products });
    }
}
