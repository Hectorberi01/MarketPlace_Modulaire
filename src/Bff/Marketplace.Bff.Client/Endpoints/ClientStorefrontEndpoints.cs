using MediatR;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Sellers.Contracts;

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
        string? ids, ICatalogModuleApi catalog, IOfferModuleApi offers, ISellerModuleApi sellers,
        ISender sender, CancellationToken ct)
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

            // Nom de boutique, résolu comme sur la fiche produit (séquentiel : le
            // DbContext est scoped).
            var sellerNames = new Dictionary<Guid, string>();
            foreach (var sid in productOffers.Select(o => o.SellerId).Distinct())
            {
                var vendeur = await sellers.GetSellerAsync(sid, ct);
                sellerNames[sid] = vendeur?.ShopName ?? "Vendeur";
            }

            var offresPubliques = productOffers
                .Select(o => new ClientCompareOffer(
                    o.Id,
                    o.ProductId,
                    o.SellerId,
                    sellerNames.TryGetValue(o.SellerId, out var nom) ? nom : "Vendeur",
                    o.Sku,
                    o.BasePriceAmount,
                    o.Currency,
                    o.Condition,
                    o.Status,
                    o.CompareAtAmount,
                    o.DiscountEndsOnUtc))
                .ToList();

            items.Add(new ClientCompareItem(product, offresPubliques, rating.IsSuccess ? rating.Value : null));
        }

        return Results.Ok(new ClientCompareResult(items));
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE ROUTE RENVOYAIT L'`OfferSummary` BRUTE, EN ANONYME.
    ///
    /// `OfferSummary` est la vue INTERNE d'une offre — « exposée aux autres modules
    /// (Cart, Pricing…) », dit son propre résumé. Elle porte, outre le prix acheteur :
    /// `SellerPrice`, `CommissionAmount`, `CommissionRate`, `ProviderFeeAmount`,
    /// `ProviderFeeRate`. Autrement dit le NET de chaque vendeur et le barème de
    /// commission de la plateforme, lisibles par n'importe qui, sans authentification,
    /// sur `GET /client/compare?ids=…`.
    ///
    /// Un concurrent y lisait la marge d'un vendeur ; un vendeur celle des autres ; et
    /// les conditions commerciales de la plateforme étaient publiques.
    ///
    /// La fiche produit, elle, projetait déjà champ par champ
    /// (`ClientCatalogEndpoints`) : c'est ce jeu de champs qui est repris ici, à un
    /// doublon près — elle émet `id` ET `offerId`, deux fois la même valeur, héritage
    /// qu'on ne reproduit pas. L'objectif reste que deux surfaces acheteur ne décrivent
    /// pas une offre de deux façons différentes.
    ///
    /// AUCUN CLIENT N'APPELAIT CETTE ROUTE : les cinq applications du groupe ont été
    /// vérifiées (elles vivent dans des dépôts VOISINS, pas dans celui-ci, qui ne
    /// contient que du C#). Rien ne casse donc ; ce qui disparaît n'aurait jamais dû
    /// sortir.
    ///
    /// LA RÈGLE QUE CE DÉFAUT ILLUSTRE : un contrat inter-modules ne se sérialise pas
    /// tel quel vers l'extérieur. Il grossit au fil des besoins internes — le barème
    /// figé a été ajouté pour la répartition du brut en commande — et chaque ajout part
    /// alors sur le réseau sans que personne ne le décide.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public sealed record ClientCompareOffer(
        Guid Id,
        Guid ProductId,
        Guid SellerId,
        string SellerName,
        string Sku,
        decimal BasePriceAmount,
        string Currency,
        string Condition,
        string Status,
        decimal? CompareAtAmount,
        DateTime? DiscountEndsOnUtc);

    public sealed record ClientCompareItem(ProductSummary Product, IReadOnlyList<ClientCompareOffer> Offers, ProductRatingSummary? Rating);
    public sealed record ClientCompareResult(IReadOnlyList<ClientCompareItem> Items);
}
