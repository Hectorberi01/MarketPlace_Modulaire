using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Commands.ApplyOfferDiscount;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;
using Marketplace.Modules.Offers.Application.Offers.Commands.RemoveOfferDiscount;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;
using Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;
using Marketplace.Modules.Offers.Application.Offers.Commands.DeleteOffer;
using Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;
using Marketplace.Modules.Offers.Application.Offers.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — offres. Liste scopée à la boutique du jeton ; chaque mutation par
/// id vérifie d'abord que l'offre appartient au vendeur (anti-IDOR).
/// </summary>
public static class SellerOfferEndpoints
{
    public static IEndpointRouteBuilder MapSellerOfferEndpoints(this IEndpointRouteBuilder app)
    {
        // Barème de prix en vigueur, en lecture seule. Voir `GetPricing`.
        var pricing = app.MapSellerGroup("/seller/pricing", "Vendeur · Offres");
        pricing.MapGet("/", GetPricing);

        var offers = app.MapSellerGroup("/seller/offers", "Vendeur · Offres");

        offers.MapGet("/", ListMineAsync);
        offers.MapGet("/{id:guid}", GetAsync);
        offers.MapPost("/", CreateAsync);
        offers.MapPatch("/{id:guid}/price", ChangePriceAsync);
        offers.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        offers.MapPatch("/{id:guid}/handling-time", SetHandlingTimeAsync);
        // Remise (promo vendeur) : appliquer / retirer.
        offers.MapPost("/{id:guid}/discount", ApplyDiscountAsync);
        offers.MapDelete("/{id:guid}/discount", RemoveDiscountAsync);
        // Retrait définitif de la mise en vente.
        offers.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    /// <summary>
    /// Barème de prix en vigueur : commission plateforme et frais du prestataire de
    /// paiement, en FRACTION du prix vendeur (0,10 = 10 %).
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI EXPOSER CES TAUX
    ///
    /// Le vendeur saisit ce qu'il veut PERCEVOIR ; la plateforme empile sa commission
    /// et les frais de paiement par-dessus pour obtenir le prix acheteur. Sans cette
    /// route, une interface qui veut montrer le détail avant enregistrement n'a qu'une
    /// option : recopier les taux dans son code. C'est ce que fait l'application mobile
    /// vendeur (`app_config.dart` : 10 % et 5 % en dur) — et au premier changement de
    /// barème, elle annonce un prix faux jusqu'à sa prochaine publication sur les
    /// stores, sans que personne ne s'en aperçoive.
    ///
    /// ATTENTION : Ce sont bien les taux GLOBAUX (`PricingOptions`, section « Pricing ») qui
    /// font foi, et non `Seller.CommissionRate` : ce champ existe encore sur l'agrégat
    /// vendeur mais n'entre plus dans le calcul — sa propre documentation le dit.
    /// Servir celui-là produirait un détail plausible et faux.
    ///
    /// Lecture seule, et sans secret : ces taux figurent déjà sur chaque offre
    /// renvoyée (`commissionAmount`, `providerFeeAmount`), dont ils se déduisent.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static IResult GetPricing(
        Marketplace.Modules.Offers.Application.Offers.PricingOptions pricing)
        => Results.Ok(new
        {
            platformCommissionRate = pricing.PlatformCommissionRate,
            providerFeeRate = pricing.ProviderFeeRate,
        });

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListOffersBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            return result.Match(o => Results.Ok(o));
        }

        return Results.Ok(await EnrichAsync(result.Value, catalog, ct));
    }

    /// <summary>Détail d'une offre du vendeur (scopé : 404 si elle ne lui appartient pas).</summary>
    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        if (!await SellerBff.OwnsOfferAsync(sender, seller.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("offer");
        }

        var result = await sender.Send(new GetOfferQuery(id), ct);
        if (result.IsFailure)
        {
            return result.Match(o => Results.Ok(o));
        }

        var enriched = await EnrichAsync(new[] { result.Value }, catalog, ct);
        return Results.Ok(enriched[0]);
    }

    /// <summary>
    /// Attache à chaque offre le NOM de son produit.
    ///
    /// <c>OfferSummary</c> ne porte qu'un <c>ProductId</c> — un GUID. Les fronts
    /// affichaient donc un générique « Produit » sur TOUTES les offres, ce qui rend
    /// l'écran inutilisable : le vendeur ne peut pas savoir quel prix il modifie.
    /// Le module Offers ne connaît pas le catalogue, et c'est très bien ainsi ;
    /// recoller les deux vues est précisément le travail du BFF — c'est déjà ce qu'il
    /// fait pour les commandes et les expéditions.
    ///
    /// Résolution DÉDUPLIQUÉE par produit : un vendeur a souvent plusieurs offres sur
    /// le même article (états, entrepôts), et il serait absurde de relire dix fois la
    /// même fiche.
    /// </summary>
    private static async Task<List<SellerOfferView>> EnrichAsync(
        IReadOnlyCollection<OfferSummary> offers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var productId in offers.Select(o => o.ProductId).Distinct())
        {
            var product = await catalog.GetProductAsync(productId, ct);
            if (product is not null)
            {
                names[productId] = product.Name;
            }
        }

        return offers.Select(o => new SellerOfferView(
            o.Id, o.ProductId,
            // Produit supprimé mais offre encore là : on le DIT, plutôt que d'afficher
            // un nom rassurant qui masquerait une incohérence de catalogue.
            names.TryGetValue(o.ProductId, out var name) ? name : "Produit introuvable",
            o.SellerId, o.Sku, o.BasePriceAmount, o.Currency, o.Condition, o.FulfillmentType,
            o.ShipFromLocationId, o.HandlingTime, o.Status,
            o.SellerPrice, o.CommissionAmount, o.ProviderFeeAmount, o.ProductPrice,
            o.CompareAtAmount, o.DiscountEndsOnUtc)).ToList();
    }

    /// <summary>
    /// Crée une offre — UNIQUEMENT sur un produit de sa propre boutique.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Le contrôle de propriété du produit manquait ici, alors que toutes les autres
    /// mutations de ce fichier passent par `GuardAsync`. Côté module,
    /// `CreateOfferCommandHandler` vérifie que le produit existe, que le vendeur
    /// existe et est actif, qu'il n'a pas déjà une offre sur ce SKU — mais jamais que
    /// le produit lui appartient. Et `Offer.Create` naît en statut `Active`.
    ///
    /// Un vendeur pouvait donc poser une offre à 1 XOF sur le produit d'un concurrent,
    /// devenir l'offre la moins chère — donc le prix affiché sur les cartes — et
    /// encaisser ses commandes. Détournement de fiche produit, sans effraction.
    ///
    /// Le contrôle est posé DANS le BFF, comme ses voisins : c'est la couche qui
    /// connaît l'identité de l'appelant. Le module, lui, ne voit qu'un `SellerId`
    /// qu'on lui passe.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> CreateAsync(
        ClaimsPrincipal user, CreateOfferRequest request, ISender sender,
        ISellerModuleApi sellers, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, request.ProductId, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LE LIEU D'EXPÉDITION AUSSI DOIT ÊTRE À SOI.
        //
        // Le produit était contrôlé, le lieu ne l'était pas — alors que le même champ
        // l'est côté stock (`SellerInventoryEndpoints.CreateItemAsync`). Une offre
        // pouvait donc s'adosser à l'entrepôt d'un concurrent, sans que le vendeur visé
        // ait le moindre moyen de le voir.
        //
        // CE QUE CE LIEU DÉCIDE RÉELLEMENT — la version précédente de ce commentaire
        // disait « les estimations de délai de livraison affichées à l'acheteur », ce
        // qui est faux : le délai annoncé vient de `ShippingRate.Eta`, résolu sur la
        // commune de DESTINATION. `ShipFromLocationId` sert à TROIS choses, et la
        // troisième est la plus lourde :
        //   - le groupage des expéditions, par (vendeur, entrepôt) ;
        //   - l'adresse où le coursier vient chercher le colis ;
        //   - la RÉSERVATION DU STOCK, faite sur le couple (SKU, lieu)
        //     (`TryReserveAsync(line.Sku, line.ShipFromLocationId, …)`), et sa libération.
        //
        // Une offre adossée à l'entrepôt d'autrui n'envoyait donc pas seulement un
        // coursier chez un tiers : elle consommait la réservation de stock de ce tiers.
        // Le commentaire d'origine sous-estimait son propre argument.
        // ─────────────────────────────────────────────────────────────────────────
        if (!await SellerBff.OwnsLocationAsync(sender, seller.Id, request.ShipFromLocationId, ct))
        {
            return SellerBff.NotFoundOrForbidden("location");
        }

        var result = await sender.Send(new CreateOfferCommand(
            request.ProductId, seller.Id, request.Sku, request.SellerPrice, request.Currency,
            request.Condition, request.FulfillmentType, request.ShipFromLocationId, request.HandlingTime), ct);

        return result.Match(id => Results.Ok(new { offerId = id }));
    }

    private static async Task<IResult> ChangePriceAsync(ClaimsPrincipal user, Guid id, PriceRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferPriceCommand(id, request.SellerPrice), ct));

    private static async Task<IResult> ChangeStatusAsync(ClaimsPrincipal user, Guid id, StatusRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ChangeOfferStatusCommand(id, request.Status), ct));

    private static async Task<IResult> SetHandlingTimeAsync(ClaimsPrincipal user, Guid id, HandlingTimeRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new SetHandlingTimeCommand(id, request.HandlingTime), ct));

    private static async Task<IResult> ApplyDiscountAsync(ClaimsPrincipal user, Guid id, DiscountRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new ApplyOfferDiscountCommand(id, request.Type, request.Value, request.EndsOnUtc), ct));

    private static async Task<IResult> RemoveDiscountAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new RemoveOfferDiscountCommand(id), ct));

    /// <summary>
    /// Supprime définitivement une mise en vente.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI CETTE ROUTE MANQUAIT — ET POURQUOI ELLE MANQUAIT VRAIMENT
    ///
    /// Le vendeur pouvait créer une offre, en changer le prix, la suspendre… mais
    /// jamais s'en défaire. `IOfferRepository.Remove` existait pourtant, sans aucun
    /// appelant. Conséquence directe : `SellerCatalogEndpoints.DeleteAsync` refuse
    /// désormais de supprimer un produit qui porte des offres, ce qui rendait TOUT
    /// produit ayant été mis en vente une fois définitivement indestructible — le
    /// message d'erreur demandait une action qu'aucune surface ne permettait.
    ///
    /// Suspendre (`Paused`) n'est pas supprimer : l'offre reste, et continue de
    /// bloquer la suppression du produit.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> DeleteAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct,
            () => sender.Send(new DeleteOfferCommand(id), ct));

    /// <summary>
    /// Résout le vendeur, vérifie SON DROIT DE VENDRE puis la propriété de l'offre,
    /// et exécute la commande.
    ///
    /// Toutes les routes qui passent par ici écrivent : changer un prix, appliquer ou
    /// retirer une remise, activer ou mettre en pause, supprimer. Le contrôle de
    /// statut manquait sur les quatre — seule la CRÉATION d'une offre le portait,
    /// dans le handler. Autrement dit, un vendeur suspendu ne pouvait pas ouvrir une
    /// offre neuve, mais il pouvait republier celles qu'il avait en pause et casser
    /// ses prix.
    /// </summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid offerId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!await SellerBff.OwnsOfferAsync(sender, seller!.Id, offerId, ct))
        {
            return SellerBff.NotFoundOrForbidden("offer");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Offre vue par le vendeur : l'<c>OfferSummary</c> du module, plus le nom du
    /// produit. Mêmes champs, même ordre — l'ajout est purement additif, les clients
    /// déjà déployés continuent de désérialiser sans broncher.
    /// </summary>
    public sealed record SellerOfferView(
        Guid Id,
        Guid ProductId,
        string ProductName,
        Guid SellerId,
        string Sku,
        decimal BasePriceAmount,
        string Currency,
        string Condition,
        string FulfillmentType,
        Guid ShipFromLocationId,
        int HandlingTime,
        string Status,
        decimal SellerPrice,
        decimal CommissionAmount,
        decimal ProviderFeeAmount,
        decimal ProductPrice,
        decimal? CompareAtAmount,
        DateTime? DiscountEndsOnUtc);

    public sealed record CreateOfferRequest(
        Guid ProductId, string Sku, decimal SellerPrice, string Currency, string Condition,
        string FulfillmentType, Guid ShipFromLocationId, int HandlingTime);

    public sealed record PriceRequest(decimal SellerPrice);
    public sealed record StatusRequest(string Status);
    public sealed record HandlingTimeRequest(int HandlingTime);
    public sealed record DiscountRequest(string Type, decimal Value, DateTime? EndsOnUtc);
}
