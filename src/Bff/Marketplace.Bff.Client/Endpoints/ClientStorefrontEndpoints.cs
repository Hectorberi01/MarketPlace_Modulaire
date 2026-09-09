using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// BFF Web — surfaces propres au web (enrichi). Comparateur multi-produits
/// (jusqu'à 4) agrégé en un appel, et emplacements SEO/contenu marketing
/// (renvoyés en 501 tant que le module Content n'est pas exposé).
/// </summary>
public static class ClientStorefrontEndpoints
{
    private const int MaxCompareItems = 4;

    public static IEndpointRouteBuilder MapClientStorefrontEndpoints(this IEndpointRouteBuilder app)
    {
        var storefront = app.MapGroup("/client").WithTags("Client · Vitrine");

        storefront.MapGet("/compare", CompareAsync).AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════
        // DEUX BOUCHONS DU BFF WEB N'ONT PAS ÉTÉ REPRIS ICI.
        //
        // `GET /content/{slug}` et `GET /feed/products` renvoyaient 501 : ils
        // annonçaient un module Content et un export catalogue qui n'existent pas.
        // Une route qui promet et ne tient pas coûte plus qu'une route absente — un
        // client la découvre, l'intègre, et ne comprend le 501 qu'en production.
        //
        // `/content/{slug}` aurait de surcroît COLLISIONNÉ avec `/content/{slot}`,
        // celui-là bien réel (ClientContentEndpoints). Deux routes de même forme sur
        // le même hôte : ASP.NET lève une AmbiguousMatchException au premier appel,
        // pas au démarrage. La panne serait apparue en production, sur une route que
        // personne n'aurait soupçonnée.
        // ═════════════════════════════════════════════════════════════════════

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

        var items = new List<ClientCompareItem>(productIds.Count);
        foreach (var id in productIds)
        {
            // Anonyme : même filtre que la fiche produit. Comparer un brouillon avec
            // un article en vente n'aurait aucun sens, et le publierait au passage.
            var product = await catalog.GetPubliclyVisibleProductAsync(id, ct);
            if (product is null)
            {
                continue; // introuvable : ignoré pour ne pas casser la comparaison.
            }

            var productOffers = await offers.GetOffersByProductAsync(id, ct);
            var rating = await sender.Send(new GetProductRatingQuery(id), ct);
            items.Add(new ClientCompareItem(product, productOffers, rating.IsSuccess ? rating.Value : null));
        }

        return Results.Ok(new ClientCompareResult(items));
    }

    public sealed record ClientCompareItem(ProductSummary Product, IReadOnlyList<OfferSummary> Offers, ProductRatingSummary? Rating);
    public sealed record ClientCompareResult(IReadOnlyList<ClientCompareItem> Items);
}
