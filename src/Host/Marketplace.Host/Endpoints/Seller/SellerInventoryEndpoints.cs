using MediatR;
using System.Security.Claims;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Catalog.Application.Products.Queries.ListProductsBySeller;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Inventory.Application.Locations.Commands;
using Marketplace.Modules.Inventory.Application.Locations.Queries;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Queries;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — lieux d'expédition &amp; stock. Le lieu est créé au nom de la
/// boutique du jeton (OwnerId = SellerId) ; son id sert ensuite de
/// « ShipFromLocationId » à la création d'offre.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LA PROPRIÉTÉ D'UN ARTICLE DE STOCK SE DÉDUIT DE SA LOCALISATION.
///
/// Le module Inventory ne rattache pas les articles à un vendeur : il ne connaît que
/// des localisations, qui portent un `OwnerId`. Ce fichier affirmait donc que
/// l'ownership « relève du module » — or le module ne le vérifiait nulle part, et le
/// garde local se contentait de constater que l'appelant possédait *une* boutique.
///
/// Résultat : `POST /seller/inventory/items/{id}/adjust {delta:-99999}` mettait en
/// rupture le catalogue de n'importe quel concurrent. Et il n'y avait même pas à
/// deviner les identifiants — `GET /by-sku/{sku}` les livrait, sans authentification,
/// à partir d'un SKU public exposé par le BFF Mobile.
///
/// Le chaînage article → localisation → `OwnerId` reconstitue la propriété sans
/// toucher au module. C'est un aller-retour de plus par écriture ; à ce prix, chaque
/// mutation par id est enfin scopée.
///
/// La vraie correction est structurelle : porter le `SellerId` sur l'article lui-même.
/// Elle demande une migration et la reprise des données, donc elle attendra — mais
/// elle rendrait ces gardes inutiles plutôt que nécessaires.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class SellerInventoryEndpoints
{
    public static IEndpointRouteBuilder MapSellerInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var locations = app.MapSellerGroup("/api/sellers/locations", "Vendeur · Lieux");
        locations.MapGet("/", ListLocationsAsync);
        locations.MapPost("/", CreateLocationAsync);
        locations.MapPut("/{id:guid}", UpdateLocationAsync);
        locations.MapDelete("/{id:guid}", DeleteLocationAsync);

        var stock = app.MapSellerGroup("/api/sellers/inventory", "Vendeur · Stock");
        stock.MapGet("/", ListMineAsync);
        stock.MapPost("/items", CreateItemAsync);
        stock.MapPost("/items/{id:guid}/receive", ReceiveAsync);
        stock.MapPost("/items/{id:guid}/adjust", AdjustAsync);
        stock.MapPut("/items/{id:guid}/reorder-threshold", SetThresholdAsync);
        stock.MapGet("/availability/{sku}", AvailabilityAsync);
        stock.MapGet("/by-sku/{sku}", ListBySkuAsync);

        return app;
    }

    /// <summary>
    /// TOUT le stock de la boutique du jeton, toutes localisations confondues.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Cette route manquait, et son absence était structurante : on ne pouvait
    /// consulter du stock qu'en partant d'un SKU précis (`/by-sku/{sku}`). Un écran
    /// « Stock » devait donc appeler cette route une fois par référence du catalogue —
    /// autant de requêtes que de SKU, à chaque affichage — pour reconstituer une liste
    /// que la base sait produire d'un coup.
    ///
    /// Le filtrage par localisation se fait EN BASE, via `ListInventoryByLocationsQuery`.
    /// Il repose sur le même chaînage que les gardes de ce fichier : article →
    /// localisation → `OwnerId`. Aucun article d'un concurrent ne peut donc entrer dans
    /// la réponse, pour la même raison qu'aucun ne peut être modifié.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        // ON INTERROGE LE MODULE DIRECTEMENT, sans passer par `SellerLocationIdsAsync`.
        //
        // Ce helper renvoie un ensemble VIDE quand la requête échoue. C'est le bon
        // comportement sur les chemins d'ÉCRITURE, où il fait échouer fermé : aucune
        // localisation connue, donc aucune mutation autorisée.
        //
        // En LECTURE, il produirait l'inverse : une panne du service des lieux
        // deviendrait un 200 avec une liste vide, que l'écran affiche « aucune référence
        // suivie ». Un vendeur conclurait que son stock a disparu. On distingue donc
        // explicitement l'échec de l'absence.
        var locations = await sender.Send(new ListFulfillmentLocationsQuery(seller.Id), ct);
        if (locations.IsFailure)
        {
            return locations.Match(l => Results.Ok(l));
        }

        var mine = locations.Value.Select(l => l.Id).ToList();
        if (mine.Count == 0)
        {
            return Results.Ok(Array.Empty<InventoryItemSummary>());
        }

        return (await sender.Send(new ListInventoryByLocationsQuery(mine), ct)).Match(i => Results.Ok(i));
    }

    /// <summary>
    /// Articles de stock d'un SKU, RESTREINTS AUX LOCALISATIONS DU VENDEUR.
    ///
    /// Cette route n'avait ni `ClaimsPrincipal` ni résolution de vendeur : elle rendait
    /// les identifiants, localisations et quantités de tous les vendeurs pour n'importe
    /// quel SKU — et les SKU sont publics côté acheteur.
    /// </summary>
    private static async Task<IResult> ListBySkuAsync(
        ClaimsPrincipal user, string sku, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListInventoryBySkuQuery(sku), ct);
        if (result.IsFailure)
        {
            return result.Match(items => Results.Ok(items));
        }

        var mine = await SellerLocationIdsAsync(sender, seller.Id, ct);
        return Results.Ok(result.Value.Where(i => mine.Contains(i.LocationId)).ToList());
    }

    /// <summary>Liste les lieux d'expédition de la boutique du jeton (OwnerId = SellerId).</summary>
    private static async Task<IResult> ListLocationsAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListFulfillmentLocationsQuery(seller.Id), ct)).Match(l => Results.Ok(l));
    }

    /// <summary>Crée un lieu d'expédition rattaché à la boutique du jeton (OwnerId = SellerId).</summary>
    private static async Task<IResult> CreateLocationAsync(ClaimsPrincipal user, LocationRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ÉCRITURE : un lieu d'expédition est l'outillage de la vente — il devient le
        // « ShipFromLocationId » des offres. Une boutique qui n'a plus le droit de
        // vendre n'a pas à en monter de nouveaux. Voir SellerRights.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await sender.Send(new CreateFulfillmentLocationCommand(
            "SellerAddress", seller!.Id, request.Commune, request.Quartier, request.Landmark,
            request.Line, request.Latitude, request.Longitude), ct);

        return result.Match(id => Results.Ok(new { locationId = id }));
    }

    /// <summary>
    /// Corrige l'adresse d'un lieu d'expédition de la boutique du jeton.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE ROUTE MANQUAIT, ET SON ABSENCE ENFERMAIT LE VENDEUR.
    ///
    /// Seuls `GET`, `POST` et `DELETE` étaient montés. Une faute dans un quartier, un
    /// point de repère devenu faux, un déménagement : la seule manœuvre était de
    /// supprimer et recréer — ce que le module REFUSE tant qu'une référence est suivie
    /// à cet endroit (`DeleteFulfillmentLocationCommandHandler`). Un vendeur avec du
    /// stock ne pouvait donc rien corriger du tout, et l'adresse fausse partait aux
    /// coursiers à chaque commande.
    ///
    /// Le nécessaire existait déjà côté module — `UpdateLocationAddressCommand` et
    /// `FulfillmentLocation.UpdateAddress` — et n'avait simplement aucune porte.
    ///
    /// LA COMMANDE NE PORTE PAS D'`OwnerId`, contrairement à la suppression : la
    /// propriété est donc vérifiée ICI, par le même chaînage que les écritures de
    /// stock (lieu → `OwnerId`), avant d'envoyer. Sans cela, un vendeur réécrivait
    /// l'adresse de l'entrepôt d'un concurrent en connaissant son identifiant.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> UpdateLocationAsync(
        ClaimsPrincipal user, Guid id, LocationRequest request, ISender sender,
        ISellerModuleApi sellers, CancellationToken ct)
    {
        // ÉCRITURE : l'adresse d'expédition pilote ce qui est livrable et à quel tarif.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var mine = await SellerLocationIdsAsync(sender, seller!.Id, ct);
        if (!mine.Contains(id))
        {
            return SellerBff.NotFoundOrForbidden("location");
        }

        var result = await sender.Send(new UpdateLocationAddressCommand(
            id, request.Commune, request.Quartier, request.Landmark,
            request.Line, request.Latitude, request.Longitude), ct);

        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Supprime un lieu d'expédition de la boutique du jeton. La suppression passe
    /// l'OwnerId (= SellerId) à la commande : le module ne supprime que si le lieu
    /// appartient bien à cette boutique (anti-IDOR ; 404 normalisé sinon).
    /// </summary>
    private static async Task<IResult> DeleteLocationAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ÉCRITURE, par symétrie avec la création : supprimer un lieu coupe la source
        // d'expédition des offres qui s'y adossent. Laisser ce geste à une boutique
        // suspendue reviendrait à lui laisser désorganiser sa logistique pendant
        // l'instruction — sans qu'elle puisse la remonter derrière.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await sender.Send(new DeleteFulfillmentLocationCommand(id, seller!.Id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Ouvre une référence en stock.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE SKU N'ÉTAIT VÉRIFIÉ NULLE PART, ET IL EST FOURNI PAR L'APPELANT.
    ///
    /// Seule la propriété du LIEU était contrôlée. `CreateInventoryItemCommand` ne
    /// regarde, lui, que le format du SKU et son unicité au couple (sku, lieu) : le
    /// catalogue n'était jamais consulté. Deux conséquences, de gravités très
    /// différentes.
    ///
    /// LA FAUTE DE FRAPPE, D'ABORD. Le vendeur tape TSHIRT-BLU-M au lieu de
    /// TSHIRT-BLEU-M : « Référence suivie », vingt unités à l'écran, et son offre — qui
    /// porte le vrai SKU — reste à zéro. Aucun écran ne rapproche jamais les deux
    /// listes, donc rien ne le lui dira. C'est le contrôle que `CreateOfferCommandHandler`
    /// applique déjà aux offres, et qui manquait ici.
    ///
    /// L'INJECTION INTER-VENDEURS, ENSUITE, ET C'EST LA VRAIE RAISON D'ÊTRE DE CE BLOC.
    /// Les SKU sont publics côté acheteur. Un vendeur pouvait saisir le SKU d'un
    /// CONCURRENT dans son PROPRE entrepôt avec `onHand = 999` : la ligne était créée,
    /// parfaitement dans son périmètre d'écriture. Or `InventoryModuleApi.IsInStockAsync`
    /// agrège toutes les localisations, TOUS VENDEURS CONFONDUS : l'article du
    /// concurrent, réellement en rupture, continuait de passer le contrôle de mise au
    /// panier, et la commande échouait plus tard à la réservation — elle, bien scopée.
    /// Le concurrent voyait ses paniers échouer sans jamais pouvoir observer la ligne
    /// fantôme, logée dans un entrepôt qui ne lui appartient pas.
    ///
    /// LE CONTRÔLE ACCEPTE DEUX PREUVES D'APPARTENANCE, ET IL EN FAUT DEUX.
    ///
    ///   • une OFFRE du vendeur sur ce SKU — le contrôle de `AvailabilityAsync` ;
    ///   • ou une VARIANTE d'un de ses propres produits portant ce SKU.
    ///
    /// La seconde n'est pas un assouplissement de confort : sans elle, on casserait un
    /// parcours réel. L'app vendeur Flutter propose « déclarer le stock » depuis chaque
    /// déclinaison d'une fiche produit (`product_detail_screen`), donc AVANT toute mise
    /// en vente — et c'est un ordre légitime : on reçoit la marchandise, puis on la met
    /// en vente. N'accepter que l'offre aurait rendu ce bouton inutilisable, avec un 400
    /// que l'écran mobile n'explique pas.
    ///
    /// Les deux ferment S2 de la même manière : le SKU d'un concurrent n'est ni dans
    /// mes offres, ni dans mes variantes.
    ///
    /// CE QUE CELA COÛTE : une lecture de la liste des produits du vendeur quand il n'y
    /// a pas d'offre. C'est un appel par ouverture de référence, pas un chemin chaud, et
    /// on ne le paie que dans le cas minoritaire.
    ///
    /// ON NORMALISE AVANT DE COMPARER, mais pas pour la raison qu'on croirait :
    /// `GetOffersBySkuAsync` normalise déjà son entrée (`Sku.Create`), et la commande
    /// aussi. La normalisation locale sert à la comparaison qu'on fait NOUS — celle des
    /// variantes, qui porte sur des chaînes venues de Catalog, lequel a sa propre
    /// représentation du contrat et ne garantit pas la casse.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> CreateItemAsync(
        ClaimsPrincipal user, CreateItemRequest request, ISender sender, ISellerModuleApi sellers,
        IOfferModuleApi offers, CancellationToken ct)
    {
        // ÉCRITURE : ouvrir une référence en stock, c'est déclarer de la marchandise
        // disponible à la vente. Voir SellerRights.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        // La localisation d'accueil doit appartenir au vendeur : sans ce contrôle, on
        // rattachait du stock à l'entrepôt d'un concurrent, faussant ses alertes de
        // réapprovisionnement.
        var mine = await SellerLocationIdsAsync(sender, seller!.Id, ct);
        if (!mine.Contains(request.LocationId))
        {
            return SellerBff.NotFoundOrForbidden("location");
        }

        var normalizedSku = (request.Sku ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedSku.Length == 0)
        {
            return Results.Problem(
                title: "inventory.sku_required",
                detail: "Le SKU est obligatoire.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Le format est vérifié AVANT l'appartenance : un SKU mal formé ne trouve rien,
        // et le message « aucune de vos références » enverrait le vendeur chercher une
        // fiche manquante là où le problème est un caractère interdit.
        if (!SkuPattern.IsMatch(normalizedSku))
        {
            return Results.Problem(
                title: "inventory.sku_invalid",
                detail: "SKU invalide : lettres, chiffres, tirets et underscores uniquement, 64 caractères au plus.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var aUneOffre = (await offers.GetOffersBySkuAsync(normalizedSku, ct)).Any(o => o.SellerId == seller!.Id);
        if (!aUneOffre && !await SellerOwnsVariantAsync(sender, seller!.Id, normalizedSku, ct))
        {
            // 400 ET NON 404 : le vendeur a le droit d'être ici, c'est sa saisie qui est
            // fautive, et il doit pouvoir la corriger. Le message ne dit PAS si ce SKU
            // existe ailleurs — l'annoncer confirmerait à un vendeur la référence exacte
            // d'un concurrent, qui est exactement ce qu'on vient de fermer.
            return Results.Problem(
                title: "inventory.sku_not_in_catalogue",
                detail: $"Le SKU « {normalizedSku} » ne correspond à aucune déclinaison ni à aucune "
                    + "mise en vente de votre catalogue. Vérifiez l'orthographe.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(new CreateInventoryItemCommand(normalizedSku, request.LocationId, request.OnHand, request.ReorderThreshold), ct);
        return result.Match(id => Results.Ok(new { inventoryItemId = id }));
    }

    private static async Task<IResult> ReceiveAsync(ClaimsPrincipal user, Guid id, QuantityRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new ReceiveStockCommand(id, request.Quantity), ct));

    private static async Task<IResult> AdjustAsync(ClaimsPrincipal user, Guid id, DeltaRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new AdjustStockCommand(id, request.Delta), ct));

    private static async Task<IResult> SetThresholdAsync(ClaimsPrincipal user, Guid id, ThresholdRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardItemAsync(user, sender, sellers, id, ct, () => sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct));

    /// <summary>
    /// Disponibilité agrégée d'un SKU — uniquement pour un SKU que le vendeur propose.
    ///
    /// La requête sous-jacente est globale. Sans ce filtre, un vendeur surveillait en
    /// continu le stock disponible de ses concurrents : une donnée concurrentielle
    /// directe, obtenue à partir d'un SKU public.
    /// </summary>
    private static async Task<IResult> AvailabilityAsync(
        ClaimsPrincipal user, string sku, ISender sender, ISellerModuleApi sellers,
        IOfferModuleApi offers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var sellsThisSku = (await offers.GetOffersBySkuAsync(sku, ct)).Any(o => o.SellerId == seller.Id);
        if (!sellsThisSku)
        {
            return SellerBff.NotFoundOrForbidden("sku");
        }

        return (await sender.Send(new GetAvailabilityQuery(sku), ct)).Match(a => Results.Ok(a));
    }

    /// <summary>
    /// Exige une boutique EN DROIT DE VENDRE ET que l'article de stock lui appartienne,
    /// puis exécute.
    ///
    /// Remplace l'ancien `GuardSellerAsync`, qui ne vérifiait que la propriété — et
    /// disait « boutique active » alors qu'il ne regardait jamais le statut. Réception,
    /// ajustement et seuil de réapprovisionnement pilotent ce qui est vendable : ce sont
    /// des écritures commerciales, pas de la consultation. Voir SellerRights.
    /// </summary>
    private static async Task<IResult> GuardItemAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid itemId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var item = await sender.Send(new GetInventoryItemQuery(itemId), ct);
        if (item.IsFailure)
        {
            return SellerBff.NotFoundOrForbidden("inventory_item");
        }

        var mine = await SellerLocationIdsAsync(sender, seller!.Id, ct);
        if (!mine.Contains(item.Value.LocationId))
        {
            // Même réponse qu'un article inexistant : distinguer les deux cas
            // confirmerait au passage l'existence du stock d'un concurrent.
            return SellerBff.NotFoundOrForbidden("inventory_item");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Vrai si une déclinaison d'un produit de CE vendeur porte ce SKU.
    ///
    /// La comparaison est ordinale sans casse, comme dans `CreateOfferCommandHandler` :
    /// les SKU viennent de Catalog, qui a sa propre représentation du contrat partagé et
    /// ne garantit pas la normalisation en majuscules.
    ///
    /// Échec de lecture du catalogue = REFUS (la méthode rend faux). Sans certitude sur
    /// l'appartenance, on ne crée pas la ligne : servir le doute comme un feu vert
    /// rouvrirait exactement l'injection qu'on ferme.
    /// </summary>
    private static async Task<bool> SellerOwnsVariantAsync(
        ISender sender, Guid sellerId, string normalizedSku, CancellationToken ct)
    {
        // UNE SEULE REQUÊTE : `ListBySellerAsync` fait déjà `Include(p => p.Variants)`,
        // donc les déclinaisons arrivent avec les produits. Recharger chaque fiche une
        // par une aurait été N appels pour une donnée déjà en main — et le résultat est
        // en cache court côté module.
        var produits = await sender.Send(new ListProductsBySellerQuery(sellerId), ct);
        if (produits.IsFailure)
        {
            return false;
        }

        return produits.Value.Any(p =>
            p.Variants.Any(v => string.Equals(v.Sku, normalizedSku, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Même motif que `Sku.Create`, recopié pour valider AVANT de chercher.</summary>
    private static readonly System.Text.RegularExpressions.Regex SkuPattern =
        new("^[A-Z0-9_-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Identifiants des localisations appartenant à cette boutique.</summary>
    private static async Task<HashSet<Guid>> SellerLocationIdsAsync(ISender sender, Guid sellerId, CancellationToken ct)
    {
        var locations = await sender.Send(new ListFulfillmentLocationsQuery(sellerId), ct);
        return locations.IsFailure
            ? new HashSet<Guid>()
            : locations.Value.Select(l => l.Id).ToHashSet();
    }

    // « Commune » accepte indifféremment le code (« abomey-calavi ») ou le libellé
    // (« Abomey-Calavi ») : les consoles envoient le code, les imports des libellés.
    public sealed record LocationRequest(
        string? Commune, string? Quartier, string? Landmark, string? Line,
        double? Latitude, double? Longitude);
    public sealed record CreateItemRequest(string Sku, Guid LocationId, int OnHand, int ReorderThreshold);
    public sealed record QuantityRequest(int Quantity);
    public sealed record DeltaRequest(int Delta);
    public sealed record ThresholdRequest(int Threshold);
}
