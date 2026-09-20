using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Marketing.Application.Campaigns;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Pricing.Application.Promotions.Queries;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Search.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// BFF CLIENT — LES ÉVÉNEMENTS.
///
/// Une campagne du module Marketing, vue par un acheteur : une vente flash, un Black
/// Friday, une rentrée. Publique, active, dans sa fenêtre, et pourvue d'une adresse —
/// c'est l'agrégat qui en décide (`EstVisiblePubliquement`), pas ce fichier.
///
/// CE QUI SORT EST UN SOUS-ENSEMBLE STRICT de ce que la console voit. `TargetSegment`
/// et `PromotionIds` décrivent le ciblage interne : les servir dirait à qui sait lire
/// quelles remises sont préparées et pour qui. La projection est donc explicite, jamais
/// le contrat d'administration renvoyé tel quel.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ClientEventEndpoints
{
    /// <summary>
    /// Nombre maximum de produits rendus pour un événement.
    ///
    /// UNE PAGE D'ÉVÉNEMENT N'EST PAS UNE PAGE DE RECHERCHE. Elle montre de quoi donner
    /// envie, pas l'intégralité d'un catalogue ; et comme la résolution lit les produits
    /// un par un (voir plus bas), une campagne visant trois mille références ferait
    /// trois mille lectures pour une seule page. La borne est donc structurelle, pas
    /// cosmétique.
    /// </summary>
    private const int MaxProduits = 48;

    public static IEndpointRouteBuilder MapClientEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/client/events").WithTags("Client · Événements").AllowAnonymous();

        events.MapGet("/", ListAsync);
        events.MapGet("/{slug}", DetailAsync);

        return app;
    }

    /// <summary>Les événements en cours, le plus proche de sa fin en premier.</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListPublicCampaignsQuery(DateTime.UtcNow), ct);

        // Une liste vide est une réponse normale : il n'y a pas toujours une opération
        // en cours. On ne rend donc jamais d'erreur ici.
        return result.IsFailure
            ? Results.Ok(Array.Empty<object>())
            : Results.Ok(result.Value.Select(Projeter).ToList());
    }

    /// <summary>Un événement et les produits qu'il concerne.</summary>
    private static async Task<IResult> DetailAsync(
        string slug,
        ISender sender,
        ICatalogModuleApi catalog,
        IOfferModuleApi offers,
        CancellationToken ct)
    {
        var campagne = await sender.Send(new GetPublicCampaignQuery(slug, DateTime.UtcNow), ct);
        if (campagne.IsFailure)
        {
            return Results.NotFound(new { error = "event_not_found" });
        }

        var produits = await ResoudreProduitsAsync(campagne.Value, sender, catalog, offers, ct);

        return Results.Ok(new
        {
            evenement = Projeter(campagne.Value),
            produits,
        });
    }

    /// <summary>La vue acheteur d'une campagne. Rien du ciblage interne n'en sort.</summary>
    private static object Projeter(CampaignSummary c) => new
    {
        slug = c.Slug,
        nom = c.Name,
        type = c.Type,
        description = c.Description,
        imageUrl = c.HeroImageUrl,
        debutUtc = c.StartUtc,
        finUtc = c.EndUtc,
    };

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// DES PROMOTIONS AUX PRODUITS : LA RÉSOLUTION, ET POURQUOI ELLE EST ICI.
    ///
    /// Une campagne ne connaît que des identifiants de promotions. Une promotion, elle,
    /// porte déjà tout ce qu'il faut : un PÉRIMÈTRE (`ScopeType`) et des CIBLES
    /// (`Targets`). Il n'y a donc rien à dénormaliser et aucun événement d'intégration à
    /// écrire — c'était l'autre option envisagée, et elle aurait créé une liste de
    /// produits à maintenir en plus de celle qui existe déjà, avec sa propre dérive.
    ///
    /// Chaque périmètre se résout avec les lectures qui existent :
    ///
    ///   Product ...... les cibles SONT des produits : lecture publique, une par une.
    ///   Category ..... une recherche filtrée sur l'ensemble des catégories visées.
    ///   Seller ....... une recherche par vendeur, une par vendeur visé.
    ///   Cart, Global . aucun produit : l'opération porte sur toute la boutique. La
    ///                  page le dira, plutôt que d'afficher une grille vide.
    ///
    /// LES LECTURES SONT SÉQUENTIELLES, et ce n'est pas une négligence : les DbContext
    /// des modules sont à portée de requête et ne sont pas utilisables en parallèle.
    /// C'est la même contrainte qui gouverne `ClientBff.WithProductImagesAsync`.
    ///
    /// UN PRODUIT NON VISIBLE EST ÉCARTÉ. `GetPubliclyVisibleProductAsync` rend nul pour
    /// un brouillon, un produit archivé ou rejeté : une promotion peut parfaitement
    /// viser un produit retiré de la vente depuis, et l'afficher dans un événement le
    /// rendrait cliquable vers une fiche qui n'existe plus.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IReadOnlyList<SearchResultItem>> ResoudreProduitsAsync(
        CampaignSummary campagne,
        ISender sender,
        ICatalogModuleApi catalog,
        IOfferModuleApi offers,
        CancellationToken ct)
    {
        var produitsVises = new List<Guid>();
        var categoriesVisees = new List<Guid>();
        var vendeursVises = new List<Guid>();

        foreach (var promotionId in campagne.PromotionIds)
        {
            var promotion = await sender.Send(new GetPromotionQuery(promotionId), ct);
            if (promotion.IsFailure)
            {
                continue;
            }

            var p = promotion.Value;
            switch (p.ScopeType)
            {
                case "Product":
                    produitsVises.AddRange(p.Targets);
                    break;
                case "Category":
                    categoriesVisees.AddRange(p.Targets);
                    break;
                case "Seller":
                    vendeursVises.AddRange(p.Targets);
                    break;
                default:
                    // Cart et Global : rien à lister.
                    break;
            }
        }

        var resultats = new List<SearchResultItem>();
        var dejaVus = new HashSet<Guid>();

        // 1. Les produits visés nommément.
        foreach (var produitId in produitsVises.Distinct())
        {
            if (resultats.Count >= MaxProduits)
            {
                break;
            }

            var produit = await catalog.GetPubliclyVisibleProductAsync(produitId, ct);
            if (produit is null || !dejaVus.Add(produit.Id))
            {
                continue;
            }

            resultats.Add(await EnVignetteAsync(produit, offers, ct));
        }

        // 2. Les catégories, en une seule recherche : l'index les filtre déjà en lot.
        if (categoriesVisees.Count > 0 && resultats.Count < MaxProduits)
        {
            var page = await sender.Send(
                new SearchProductsQuery(null, null, null, 1, MaxProduits - resultats.Count, CategoryIds: categoriesVisees.Distinct().ToList()),
                ct);
            if (page.IsSuccess)
            {
                AjouterSansDoublon(resultats, dejaVus, await EnrichirAsync(page.Value, catalog, offers, ct));
            }
        }

        // 3. Les vendeurs, une recherche chacun : l'index ne filtre qu'un vendeur à la fois.
        foreach (var vendeurId in vendeursVises.Distinct())
        {
            if (resultats.Count >= MaxProduits)
            {
                break;
            }

            var page = await sender.Send(
                new SearchProductsQuery(null, null, null, 1, MaxProduits - resultats.Count, SellerId: vendeurId),
                ct);
            if (page.IsSuccess)
            {
                AjouterSansDoublon(resultats, dejaVus, await EnrichirAsync(page.Value, catalog, offers, ct));
            }
        }

        return resultats;
    }

    /// <summary>
    /// Enrichit une page de l'index (image principale, prix barré) comme la recherche le
    /// fait, pour qu'une grille d'événement se lise exactement comme celle d'un rayon.
    ///
    /// SEULES LES VIGNETTES VENUES DE L'INDEX PASSENT ICI. Celles construites par
    /// `EnVignetteAsync` portent déjà leur image et leur prix : les repasser dans cet
    /// enrichissement aurait relu les offres de CHAQUE produit une seconde fois —
    /// jusqu'à 96 allers-retours en base pour une page anonyme de 48 produits.
    /// </summary>
    private static async Task<IReadOnlyList<SearchResultItem>> EnrichirAsync(
        SearchResultPage page, ICatalogModuleApi catalog, IOfferModuleApi offers, CancellationToken ct)
        => (await ClientBff.WithProductImagesAsync(page, catalog, offers, ct)).Items;

    private static void AjouterSansDoublon(
        List<SearchResultItem> resultats, HashSet<Guid> dejaVus, IReadOnlyList<SearchResultItem> items)
    {
        foreach (var item in items)
        {
            if (resultats.Count >= MaxProduits)
            {
                return;
            }

            if (dejaVus.Add(item.ProductId))
            {
                resultats.Add(item);
            }
        }
    }

    /// <summary>
    /// Un produit du catalogue, mis à la forme d'une vignette de recherche.
    ///
    /// LA MÊME FORME QUE LA RECHERCHE, et pas une forme à part : la vitrine et
    /// l'application ont déjà une grille de produits qui sait afficher exactement cela.
    /// Inventer ici un objet « produit d'événement » aurait obligé à écrire une seconde
    /// carte produit, qui aurait divergé de la première au premier changement de design.
    ///
    /// Le prix vient de l'offre ACTIVE la moins chère — la même règle que partout
    /// ailleurs dans ce BFF.
    /// </summary>
    private static async Task<SearchResultItem> EnVignetteAsync(
        ProductSummary produit, IOfferModuleApi offers, CancellationToken ct)
    {
        var offres = await offers.GetOffersByProductAsync(produit.Id, ct);
        var moinsChere = offres
            .Where(o => string.Equals(o.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.BasePriceAmount)
            .FirstOrDefault();

        var image = produit.Media.FirstOrDefault(m => m.IsPrimary)?.Url ?? produit.Media.FirstOrDefault()?.Url;

        // Le prix barré vient de la MÊME offre que le prix affiché, lue une seule fois.
        // C'est ce qui permet à cette vignette de ne pas repasser par l'enrichissement.
        var enPromotion = moinsChere?.CompareAtAmount is { } origine && origine > moinsChere.BasePriceAmount;

        return new SearchResultItem(
            produit.Id,
            produit.SellerId,
            produit.CategoryId,
            produit.Name,
            produit.Slug,
            moinsChere?.BasePriceAmount,
            moinsChere?.Currency,
            // La note et le nombre d'avis ne sont pas lus ici : ils viendraient d'un
            // module de plus, une lecture de plus par produit, pour une information que
            // la carte affiche en petit. L'index de recherche les porte pour les autres
            // grilles ; celle-ci s'en passe.
            0,
            0,
            image,
            enPromotion ? moinsChere!.CompareAtAmount : null,
            enPromotion ? moinsChere!.DiscountEndsOnUtc : null);
    }
}
