using MediatR;
using System.Security.Claims;
using System.Text.Json;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Marketing.Application.Content;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Recommendations.Contracts;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Search.Contracts;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Client — accueil & découverte (/client/home, /categories, /recommendations).
/// L'écran d'accueil agrège en un seul appel (mises en avant + reco perso).
/// </summary>
public static class ClientDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapClientDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var mobile = app.MapGroup("/client").WithTags("Client · Découverte");

        mobile.MapGet("/home", HomeAsync).AllowAnonymous();
        mobile.MapGet("/recommendations", RecommendationsAsync).RequireAuthorization();

        // Catégories actives (chips d'accueil & recherche).
        mobile.MapGet("/categories", CategoriesAsync).AllowAnonymous();
        mobile.MapGet("/categories/{slug}", CategoryBySlugAsync).AllowAnonymous();

        // Bannières promotionnelles du carrousel d'accueil (pilotées par config).
        mobile.MapGet("/home/banners", BannersAsync).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Bannières du carrousel d'accueil. Source PRINCIPALE : les blocs de contenu
    /// du slot « home_banner » (gérés depuis le dashboard admin, publiés et dans
    /// leur fenêtre d'affichage). Chaque bloc porte un JSON
    /// { imageUrl, linkUrl, title }. REPLI : la section de config « HomeBanners »
    /// (compat), pour ne jamais casser un déploiement déjà configuré ainsi.
    /// Le lien peut être une route interne (« /product/{id} ») ou une URL http(s).
    /// </summary>
    private static async Task<IResult> BannersAsync(ISender sender, IConfiguration config, CancellationToken ct)
    {
        var blocks = await sender.Send(new ListContentBySlotQuery(ContentSlots.HomeBanner), ct);
        if (blocks.IsSuccess && blocks.Value.Count > 0)
        {
            var fromCms = blocks.Value
                .OrderBy(b => b.Position)
                .Select(b => ParseBanner(b.Content))
                .Where(b => b is not null)
                .ToList();
            if (fromCms.Count > 0)
            {
                return Results.Ok(fromCms);
            }
        }

        // Repli configuration (aucun bloc publié).
        var banners = config.GetSection("HomeBanners").Get<HomeBanner[]>() ?? Array.Empty<HomeBanner>();
        return Results.Ok(banners.Where(b => !string.IsNullOrWhiteSpace(b.ImageUrl)));
    }

    /// <summary>Parse le JSON d'un bloc bannière. Renvoie null si image absente/illisible.</summary>
    private static object? ParseBanner(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var imageUrl = root.TryGetProperty("imageUrl", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }
            var linkUrl = root.TryGetProperty("linkUrl", out var l) ? l.GetString() : null;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            return new { imageUrl, linkUrl, title };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record HomeBanner(string ImageUrl, string? LinkUrl, string? Title);

    /// <summary>Liste plate des catégories visibles (id, nom, slug) pour l'accueil & les filtres.</summary>
    private static async Task<IResult> CategoriesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListCategoriesQuery(), ct);
        if (result.IsFailure)
        {
            return Results.Ok(Array.Empty<object>());
        }

        // BUG CORRIGÉ : le cycle de vie d'une catégorie est Draft → PUBLISHED →
        // Archived (pas « Active », qui est le statut des PRODUITS). Filtrer sur
        // « Active » excluait donc TOUTES les catégories, même publiées — d'où une
        // liste vide côté app. On accepte « Published » (et « Active » par sécurité).
        var visible = result.Value
            .Where(c => string.Equals(c.Status, "Published", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(c => new { id = c.Id, name = c.Name, slug = c.Slug, parentId = c.ParentId, imageUrl = c.ImageUrl });

        return Results.Ok(visible);
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

    /// <summary>Écran d'accueil agrégé : sélection + mis en avant + reco perso (1 appel).</summary>
    private static async Task<IResult> HomeAsync(ClaimsPrincipal user, ISender sender, ICatalogModuleApi catalog, IOfferModuleApi offers, CancellationToken ct)
    {
        var featured = await sender.Send(new SearchProductsQuery(null, null, null, 1, 10), ct);

        // Enrichit les mises en avant avec l'image principale + l'éventuelle promotion.
        var featuredPage = featured.IsSuccess
            ? await ClientBff.WithProductImagesAsync(featured.Value, catalog, offers, ct)
            : null;

        // Section ÉDITORIALE « Mis en avant » : produits porteurs du tag `featured`.
        // Le tag est le drapeau (pas de colonne) ; le prix vient des offres.
        var promotedProducts = await catalog.ListFeaturedAsync(12, ct);
        var promoted = new List<object>();
        foreach (var prod in promotedProducts)
        {
            var offs = await offers.GetOffersByProductAsync(prod.Id, ct);
            // Offre active la moins chère : elle fixe le prix affiché ET l'éventuelle promo.
            var cheapest = offs
                .Where(o => string.Equals(o.Status, "Active", StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.BasePriceAmount)
                .FirstOrDefault() ?? (offs.Count > 0 ? offs[0] : null);
            decimal? minPrice = cheapest?.BasePriceAmount;
            var image = prod.Media.FirstOrDefault(m => m.IsPrimary)?.Url ?? prod.Media.FirstOrDefault()?.Url;
            var onSale = cheapest?.CompareAtAmount is { } c && c > cheapest.BasePriceAmount;
            promoted.Add(new
            {
                id = prod.Id,
                name = prod.Name,
                imageUrl = image,
                minPrice,
                currency = cheapest?.Currency ?? "XOF",
                compareAtPrice = onSale ? cheapest!.CompareAtAmount : null,
                discountEndsOnUtc = onSale ? cheapest!.DiscountEndsOnUtc : null,
            });
        }

        RecommendationSummary? personalized = null;
        if (ClientBff.CurrentUserId(user) is { } userId)
        {
            // Dégradation gracieuse : si la reco échoue, l'accueil reste servi.
            var reco = await sender.Send(new GetUserRecommendationsQuery(userId), ct);
            if (reco.IsSuccess)
            {
                personalized = reco.Value;
            }
        }

        return Results.Ok(new ClientHome(
            Featured: featuredPage,
            Recommendations: personalized,
            Promoted: promoted));
    }

    /// <summary>Recommandations personnalisées de l'utilisateur connecté.</summary>
    private static async Task<IResult> RecommendationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new GetUserRecommendationsQuery(userId), ct)).Match(r => Results.Ok(r));
    }

    public sealed record ClientHome(SearchResultPage? Featured, RecommendationSummary? Recommendations, IReadOnlyList<object>? Promoted);
}
