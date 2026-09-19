using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;
using Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProductVariant;
using Marketplace.Modules.Catalog.Application.Products.Commands.RemoveProductVariant;
using Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProductWithImages;
using Marketplace.Modules.Catalog.Application.Products.Commands.DeleteProduct;
using Marketplace.Modules.Catalog.Application.Products.Commands.RemoveProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.SetPrimaryProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.ReorderProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.UploadProductImage;
using Marketplace.Modules.Catalog.Application.Products.Commands.ProcessImage;
using Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;
using Marketplace.Modules.Catalog.Application.Products.Queries.ListProductsBySeller;
using Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;
using Marketplace.Modules.Catalog.Application.Brands.Queries.ListBrands;
// Suppression produit : on vérifie d'abord qu'aucune offre n'y est rattachée.
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — catalogue. Liste/lecture scopées au vendeur du jeton ; chaque
/// mutation par id vérifie que le produit appartient au vendeur (anti-IDOR).
/// </summary>
public static class SellerCatalogEndpoints
{
    public static IEndpointRouteBuilder MapSellerCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapSellerGroup("/seller/products", "Vendeur · Catalogue");

        products.MapGet("/", ListMineAsync);
        products.MapGet("/{id:guid}", GetAsync);
        // Création produit AVEC upload d'images (multipart/form-data).
        products.MapPost("/", CreateWithImagesAsync).DisableAntiforgery();
        // Traitement d'une image (détourage IA + fond blanc) AVANT création, pour
        // que le vendeur valide le rendu. Renvoie l'image traitée (JPEG).
        products.MapPost("/media/process", ProcessMediaAsync).DisableAntiforgery();
        // Le traitement d'image est-il RÉELLEMENT actif ? Voir MediaCapabilities.
        products.MapGet("/media/capabilities", MediaCapabilities);
        products.MapPut("/{id:guid}", UpdateAsync);
        products.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        products.MapPost("/{id:guid}/media", AddMediaAsync);
        // Upload d'une image (multipart) attachée au produit après création.
        products.MapPost("/{id:guid}/media/upload", UploadMediaAsync).DisableAntiforgery();
        products.MapPost("/{id:guid}/media/{mediaId:guid}/primary", SetPrimaryMediaAsync);
        products.MapPut("/{id:guid}/media/order", ReorderMediaAsync);
        products.MapDelete("/{id:guid}/media/{mediaId:guid}", RemoveMediaAsync);
        products.MapPost("/{id:guid}/variants", AddVariantAsync);
        products.MapPut("/{id:guid}/variants/{variantId:guid}", UpdateVariantAsync);
        products.MapDelete("/{id:guid}/variants/{variantId:guid}", RemoveVariantAsync);
        products.MapDelete("/{id:guid}", DeleteAsync);

        // Catégories de la plateforme (pour le sélecteur de création produit).
        var categories = app.MapSellerGroup("/seller/categories", "Vendeur · Catalogue");
        categories.MapGet("/", ListCategoriesAsync);

        // Marques de la plateforme (pour le sélecteur de création produit).
        var brands = app.MapSellerGroup("/seller/brands", "Vendeur · Catalogue");
        brands.MapGet("/", ListBrandsAsync);

        return app;
    }

    /// <summary>Catégories de la plateforme (lecture seule) pour alimenter le formulaire produit.</summary>
    private static async Task<IResult> ListCategoriesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCategoriesQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>Marques de la plateforme (lecture seule) pour alimenter le formulaire produit.</summary>
    private static async Task<IResult> ListBrandsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListBrandsQuery(), ct)).Match(b => Results.Ok(b));

    /// <summary>
    /// Produits de la boutique du jeton (l'id vendeur est déduit du token).
    ///
    /// PAGINÉE (<c>?page=&amp;pageSize=</c>) et filtrable (<c>?search=</c> sur le nom).
    /// Sans paramètre, tout est renvoyé : les appelants existants ne cassent pas.
    /// </summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct,
        int? page = null, int? pageSize = null, string? search = null)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListProductsBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            return result.Match(p => Results.Ok(p));
        }

        var products = result.Value.ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            products = products
                .Where(p => p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Tri stable AVANT le découpage : sans lui, deux pages successives
        // pourraient renvoyer le même produit et en oublier un autre.
        products = products.OrderBy(p => p.Name).ToList();

        return Results.Ok(SellerBff.Paginate(products, page, pageSize));
    }

    /// <summary>Fiche produit (scopée : 404 si elle n'appartient pas au vendeur).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var product = await catalog.GetProductAsync(id, ct);
        return product is not null && product.SellerId == seller.Id
            ? Results.Ok(product)
            : SellerBff.NotFoundOrForbidden("product");
    }

    /// <summary>
    /// Crée un produit (Draft) AVEC upload de plusieurs images (multipart/form-data).
    /// Champs : categoryId, name, description, brandId?, gtin?, ean? ; fichiers sous
    /// le champ « images ». Les images sont stockées par le service média externe ;
    /// les URLs renvoyées sont attachées au produit (la 1re devient principale).
    /// </summary>
    private static async Task<IResult> CreateWithImagesAsync(
        ClaimsPrincipal user,
        IFormFileCollection images,
        [FromForm] Guid categoryId,
        [FromForm] string name,
        [FromForm] string description,
        ISender sender,
        ISellerModuleApi sellers,
        CancellationToken ct,
        [FromForm] Guid? brandId = null,
        [FromForm] string? gtin = null,
        [FromForm] string? ean = null,
        [FromForm] Guid? productGroupId = null,
        [FromForm] string? tags = null,
        [FromForm] string? attributesJson = null)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // ÉCRITURE DE CATALOGUE : LE DROIT DE VENDRE SE VÉRIFIE ICI.
        //
        // Cette route utilisait `ResolveSellerAsync`, qui rend la boutique QUEL QUE
        // SOIT son statut. `SellerBff.ResolveSellingSellerAsync` le dit en toutes
        // lettres : « un appelant qui utilise encore ResolveSellerAsync sur une
        // écriture saute visiblement une étape ». C'était le cas ici.
        //
        // Conséquence mesurée avant correction : une boutique SUSPENDUE ou FERMÉE
        // pouvait créer des produits, y attacher des photos et des déclinaisons.
        // Pour « Closed », cela annulait la sanction — la fermeture dépublie le
        // catalogue, et il suffisait de le recréer. `SellerRights.CanSell` couvre
        // explicitement « produits, variantes, médias » ; la garde manquait, pas la
        // règle.
        // ═════════════════════════════════════════════════════════════════════════
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (images.Count == 0)
        {
            return Results.BadRequest(new { error = "Au moins une image est requise." });
        }

        var uploads = new List<ProductImageUpload>(images.Count);
        foreach (var file in images)
        {
            var check = await UploadValidation.CheckImageAsync(file, ct);
            if (check.Error is { } invalid)
            {
                return invalid;
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            // Le type RÉEL, déduit des octets — jamais `file.ContentType`, qui n'est que
            // la déclaration non vérifiée du client. C'est ce type qui sera stocké sur R2,
            // renvoyé aux navigateurs, et remis aux processeurs d'image.
            uploads.Add(new ProductImageUpload(file.FileName, check.ContentType!, ms.ToArray()));
        }

        // Attributs envoyés sous forme d'objet JSON { "clé": "valeur", ... }.
        IReadOnlyDictionary<string, string>? attributes = null;
        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            try { attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(attributesJson); }
            catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "attributesJson invalide (objet clé/valeur attendu)." }); }
        }

        // Tags : chaîne « tag1,tag2,tag3 » (binding multipart fiable, contrairement à string[]).
        var cleanTags = tags?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var result = await sender.Send(new CreateProductWithImagesCommand(
            seller!.Id, categoryId, name, description,
            brandId, string.IsNullOrWhiteSpace(gtin) ? null : gtin, string.IsNullOrWhiteSpace(ean) ? null : ean,
            productGroupId,
            attributes is { Count: > 0 } ? attributes : null,
            cleanTags is { Count: > 0 } ? cleanTags : null,
            uploads), ct);

        return result.Match(r => Results.Ok(r));
    }

    /// <summary>
    /// Dit si le détourage est RÉELLEMENT opérationnel.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Sans identifiants Cloudinary, `NullImageProcessor` renvoie l'image D'ORIGINE
    /// avec un succès. `POST /media/process` répond donc 200 avec des octets
    /// strictement inchangés, et une interface honnête n'a AUCUN moyen de distinguer
    /// « détourage réussi » de « détourage jamais tenté ». Les consoles affichaient
    /// « Détourée » sur une photo intacte ; le vendeur cliquait un bouton sans effet,
    /// et concluait que le produit était cassé.
    ///
    /// Cette route ne fuite rien : ni identifiant, ni nom de fournisseur — seulement
    /// « la fonction marche ou non », ce que l'utilisateur constate de toute façon.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static IResult MediaCapabilities(IImageProcessor processor)
        => Results.Ok(new
        {
            backgroundRemoval = processor is IImageProcessingAvailability { IsAvailable: true },
        });

    /// <summary>
    /// Traite une image (détourage IA + fond blanc) via Cloudinary et renvoie
    /// l'image traitée (JPEG), SANS créer de produit. Sert au récap « avant/après »
    /// côté app. Scopé vendeur. Champ multipart « image ».
    /// </summary>
    private static async Task<IResult> ProcessMediaAsync(
        ClaimsPrincipal user,
        IFormFile image,
        ISender sender,
        ISellerModuleApi sellers,
        CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // NE CRÉE RIEN, MAIS DÉPENSE — D'OÙ LA MÊME GARDE QUE LES ÉCRITURES.
        //
        // Cette route n'écrit pas dans le catalogue : elle expédie des octets à un
        // service de traitement d'image FACTURÉ et rend le résultat. Laissée sur
        // `ResolveSellerAsync`, une boutique suspendue ou fermée pouvait la solliciter
        // en boucle — et le résultat ne lui servait à rien, puisque la création de
        // produit à laquelle elle prépare est désormais refusée.
        //
        // Refuser ici évite au vendeur d'attendre un détourage dont il ne pourra rien
        // faire, et à la plateforme d'en payer le coût.
        // ═════════════════════════════════════════════════════════════════════════
        // `_` : cette route n'a pas besoin de l'identité de la boutique, seulement de
        // son droit. La déconstruire sous un nom inutilisé inviterait à s'en servir.
        var (_, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var check = await UploadValidation.CheckImageAsync(image, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);

        // Ce contenu part vers un PROCESSEUR D'IMAGE (Cloudinary). Livrer des octets
        // arbitraires à un décodeur d'images en lui annonçant un format choisi par
        // l'attaquant, c'est le vecteur classique des CVE de bibliothèques graphiques.
        var result = await sender.Send(
            new ProcessImageCommand(image.FileName, check.ContentType!, ms.ToArray()), ct);

        return result.Match(r => Results.File(r.Content, r.ContentType));
    }

    private static async Task<IResult> UpdateAsync(ClaimsPrincipal user, Guid id, UpdateProductRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new UpdateProductCommand(
                id, request.Name, request.Description, request.BrandId, request.Gtin,
                request.Ean, request.ProductGroupId, request.Attributes, request.Tags,
                request.CategoryId), ct));

    private static async Task<IResult> ChangeStatusAsync(ClaimsPrincipal user, Guid id, StatusRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
    {
        // La publication (mise en vitrine) est réservée à l'administration : un
        // vendeur ne peut pas activer/rendre visible son produit lui-même. Il
        // peut le repasser en brouillon ou l'archiver (retrait de la vente).
        if (string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = "La publication d'un produit est soumise à validation de l'administration." },
                statusCode: 403);
        }

        return await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new ChangeProductStatusCommand(id, request.Status), ct));
    }

    private static async Task<IResult> AddMediaAsync(ClaimsPrincipal user, Guid id, MediaRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // ÉCRITURE DE CATALOGUE : LE DROIT DE VENDRE SE VÉRIFIE ICI.
        //
        // Cette route utilisait `ResolveSellerAsync`, qui rend la boutique QUEL QUE
        // SOIT son statut. `SellerBff.ResolveSellingSellerAsync` le dit en toutes
        // lettres : « un appelant qui utilise encore ResolveSellerAsync sur une
        // écriture saute visiblement une étape ». C'était le cas ici.
        //
        // Conséquence mesurée avant correction : une boutique SUSPENDUE ou FERMÉE
        // pouvait créer des produits, y attacher des photos et des déclinaisons.
        // Pour « Closed », cela annulait la sanction — la fermeture dépublie le
        // catalogue, et il suffisait de le recréer. `SellerRights.CanSell` couvre
        // explicitement « produits, variantes, médias » ; la garde manquait, pas la
        // règle.
        // ═════════════════════════════════════════════════════════════════════════
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller!.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await sender.Send(new AddProductMediaCommand(
                id, request.Url, request.Type ?? "Image", request.AltText, request.IsPrimary), ct))
            .Match(mediaId => Results.Ok(new { mediaId }));
    }

    /// <summary>Téléverse une image (multipart, champ « file ») et l'attache au produit. Scopé vendeur.</summary>
    private static async Task<IResult> UploadMediaAsync(
        ClaimsPrincipal user, Guid id, IFormFile file, ISender sender,
        ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct,
        [FromForm] string? alt = null, [FromForm] bool isPrimary = false)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // ÉCRITURE DE CATALOGUE : LE DROIT DE VENDRE SE VÉRIFIE ICI.
        //
        // Cette route utilisait `ResolveSellerAsync`, qui rend la boutique QUEL QUE
        // SOIT son statut. `SellerBff.ResolveSellingSellerAsync` le dit en toutes
        // lettres : « un appelant qui utilise encore ResolveSellerAsync sur une
        // écriture saute visiblement une étape ». C'était le cas ici.
        //
        // Conséquence mesurée avant correction : une boutique SUSPENDUE ou FERMÉE
        // pouvait créer des produits, y attacher des photos et des déclinaisons.
        // Pour « Closed », cela annulait la sanction — la fermeture dépublie le
        // catalogue, et il suffisait de le recréer. `SellerRights.CanSell` couvre
        // explicitement « produits, variantes, médias » ; la garde manquait, pas la
        // règle.
        // ═════════════════════════════════════════════════════════════════════════
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller!.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        var check = await UploadValidation.CheckImageAsync(file, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        return (await sender.Send(new UploadProductImageCommand(
                id, file.FileName, check.ContentType!, ms.ToArray(), alt, isPrimary), ct))
            .Match(url => Results.Ok(new { url }));
    }

    /// <summary>Définit l'image principale du produit. Scopé vendeur.</summary>
    private static async Task<IResult> SetPrimaryMediaAsync(ClaimsPrincipal user, Guid id, Guid mediaId, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new SetPrimaryProductMediaCommand(id, mediaId), ct));

    /// <summary>Réordonne les images du produit selon la liste fournie. Scopé vendeur.</summary>
    private static async Task<IResult> ReorderMediaAsync(ClaimsPrincipal user, Guid id, ReorderRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new ReorderProductMediaCommand(id, request.OrderedMediaIds), ct));

    /// <summary>Retire un média du produit (et le supprime côté service média). Scopé vendeur.</summary>
    private static async Task<IResult> RemoveMediaAsync(ClaimsPrincipal user, Guid id, Guid mediaId, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new RemoveProductMediaCommand(id, mediaId), ct));

    private static async Task<IResult> AddVariantAsync(ClaimsPrincipal user, Guid id, VariantRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // ÉCRITURE DE CATALOGUE : LE DROIT DE VENDRE SE VÉRIFIE ICI.
        //
        // Cette route utilisait `ResolveSellerAsync`, qui rend la boutique QUEL QUE
        // SOIT son statut. `SellerBff.ResolveSellingSellerAsync` le dit en toutes
        // lettres : « un appelant qui utilise encore ResolveSellerAsync sur une
        // écriture saute visiblement une étape ». C'était le cas ici.
        //
        // Conséquence mesurée avant correction : une boutique SUSPENDUE ou FERMÉE
        // pouvait créer des produits, y attacher des photos et des déclinaisons.
        // Pour « Closed », cela annulait la sanction — la fermeture dépublie le
        // catalogue, et il suffisait de le recréer. `SellerRights.CanSell` couvre
        // explicitement « produits, variantes, médias » ; la garde manquait, pas la
        // règle.
        // ═════════════════════════════════════════════════════════════════════════
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller!.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await sender.Send(new AddProductVariantCommand(
                id, request.Sku, request.Attributes, request.Barcode, request.WeightGrams,
                request.LengthMm, request.WidthMm, request.HeightMm), ct))
            .Match(variantId => Results.Ok(new { variantId }));
    }

    /// <summary>
    /// Met à jour une variante du produit. Scopé vendeur.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// RENOMMER UN SKU DÉJÀ MIS EN VENTE EST REFUSÉ. C'ÉTAIT UNE VENTE À DÉCOUVERT.
    ///
    /// `Product.UpdateVariant` valide le format du SKU et son unicité DANS LE PRODUIT,
    /// puis écrit — et ne lève aucun événement. Ni `Offer.VariantSku` ni
    /// `InventoryItem.Sku` ne bougent : les trois modules portaient la même référence,
    /// deux gardaient l'ancienne.
    ///
    /// LA SUITE, VÉRIFIÉE, EST PIRE QUE DEUX LIGNES DÉSACCORDÉES. `CreateOfferDialog`
    /// calcule les SKU encore libres en comparant les variantes aux offres : la
    /// variante renommée apparaît comme libre, le vendeur crée une SECONDE mise en
    /// vente dessus — sans aucun article de stock derrière. Et
    /// `InventoryModuleApi.IsInStockAsync` répond « disponible » pour un SKU qui n'est
    /// suivi nulle part. La fiche porte alors deux offres dont l'une est fantôme, et
    /// cette offre-là accepte les commandes.
    ///
    /// POURQUOI INTERDIRE PLUTÔT QUE PROPAGER. Renommer proprement demanderait de
    /// réécrire le SKU dans Offers et dans Inventory, de revérifier deux unicités, et
    /// de décider du sort des lignes de commande en cours qui portent l'ancienne
    /// chaîne — un chantier de trois modules, pas un correctif d'écran. Le refus,
    /// lui, est exact aujourd'hui.
    ///
    /// LE RENOMMAGE RESTE OUVERT TANT QU'IL N'ENGAGE RIEN, et c'est le cas qui compte :
    /// corriger une coquille sur une variante pas encore mise en vente. Ce qui est
    /// fermé, c'est le renommage d'une référence qui porte déjà une offre.
    ///
    /// LE CONTRÔLE EST ICI, DANS LE BFF, comme celui de la suppression de produit :
    /// c'est la seule couche qui voit à la fois le catalogue et les offres.
    ///
    /// CE QUI N'EST PAS VÉRIFIÉ, ET IL FAUT LE SAVOIR : une ligne de STOCK sans offre.
    /// Elle ne peut plus être créée — `CreateItemAsync` exige désormais que le vendeur
    /// propose le SKU — mais des lignes antérieures existent, et une offre supprimée
    /// laisse la sienne derrière. Le module Inventory n'expose aucune lecture par SKU
    /// scopée vendeur ; l'ajouter pour cette garde serait la bonne suite.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> UpdateVariantAsync(
        ClaimsPrincipal user, Guid id, Guid variantId, VariantRequest request, ISender sender,
        ICatalogModuleApi catalog, ISellerModuleApi sellers, IOfferModuleApi offers, CancellationToken ct)
    {
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var product = await catalog.GetProductAsync(id, ct);
        if (product is null || product.SellerId != seller!.Id)
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        var variant = product.Variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return SellerBff.NotFoundOrForbidden("variant");
        }

        // Même normalisation que `Sku.Create`, pour ne pas confondre un vrai renommage
        // avec un changement de casse ou un espace de bord.
        var demande = (request.Sku ?? string.Empty).Trim().ToUpperInvariant();
        var actuel = (variant.Sku ?? string.Empty).Trim().ToUpperInvariant();

        // LE FORMAT D'ABORD. Un champ vidé donne `demande == ""`, donc « différent de
        // l'actuel » : le vendeur recevait alors « ce SKU porte déjà une de vos mises en
        // vente », qui n'a rien à voir avec son problème. On laisse le domaine dire ce
        // qu'il dit déjà bien — « le SKU ne peut pas être vide ».
        if (demande.Length > 0 && demande != actuel)
        {
            var engagees = await offers.GetOffersBySkuAsync(actuel, ct);
            if (engagees.Any(o => o.SellerId == seller!.Id))
            {
                // 409 : la requête est bien formée, c'est l'état courant qui l'interdit.
                // Le message dit la seule manœuvre qui marche, plutôt que de laisser le
                // vendeur chercher pourquoi la plateforme lui refuse une correction.
                return Results.Problem(
                    title: "catalog.variant.sku_locked_by_offer",
                    detail: $"Le SKU « {actuel} » porte déjà une de vos mises en vente : le renommer "
                        + "laisserait l'offre et le stock sur l'ancienne référence. Retirez l'offre, "
                        + "renommez, puis recréez-la — ou ajoutez une nouvelle variante.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        // ON ENVOIE `demande`, PAS `request.Sku`, ET C'EST DEUX CORRECTIONS EN UNE.
        //
        // La première est une erreur de compilation que j'avais introduite : le
        // `?? string.Empty` plus haut apprend au compilateur que `request.Sku` PEUT
        // être nul — un corps JSON qui omet le champ le rend nul malgré la déclaration
        // non annulable — et l'analyse de flux propageait ce doute jusqu'ici (CS8604).
        //
        // La seconde est une asymétrie : on comparait la valeur normalisée et on
        // envoyait la brute. Sans conséquence — `Sku.Create` normalise de son côté —
        // mais deux valeurs pour une seule notion finissent toujours par diverger.
        var result = await sender.Send(
            new UpdateProductVariantCommand(id, variantId, demande, request.Attributes, request.Barcode, request.WeightGrams), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire une variante du produit. Scopé vendeur.</summary>
    private static async Task<IResult> RemoveVariantAsync(ClaimsPrincipal user, Guid id, Guid variantId, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new RemoveProductVariantCommand(id, variantId), ct));

    /// <summary>
    /// Supprime un produit — REFUSÉ tant qu'il porte des offres.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI CE GARDE-FOU
    ///
    /// `DeleteProductCommandHandler` efface le produit et ses images, publie
    /// `ProductDeletedIntegrationEvent` pour désindexer la recherche… et ne touche
    /// pas au module Offers, qui ne l'écoute pas. Les offres survivaient donc à leur
    /// produit : `EnrichAsync` (SellerOfferEndpoints) prévoit d'ailleurs déjà le cas
    /// et affiche « Produit introuvable ». Ce repli constate le dégât, il ne l'évite
    /// pas — et une offre orpheline reste en statut `Active`.
    ///
    /// Le contrôle est posé ICI, dans le BFF : c'est la seule couche qui voit à la
    /// fois le catalogue et les offres, exactement comme pour le contrôle de
    /// propriété du produit à la création d'une offre.
    ///
    /// 409 plutôt que 400 : ce n'est pas la requête qui est mal formée, c'est l'état
    /// courant qui l'interdit. Le message dit quoi faire, pas seulement que c'est non.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> DeleteAsync(ClaimsPrincipal user, Guid id, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // SEULE ÉCRITURE DE CATALOGUE LAISSÉE SANS CONTRÔLE DE STATUT, ET C'EST
        // DÉLIBÉRÉ — MAIS CE N'EST PAS TRANCHÉ.
        //
        // Les quatre autres routes qui utilisaient encore `ResolveSellerAsync` sur
        // une écriture (création de produit, ajout de média, téléversement de média,
        // ajout de déclinaison) ont été fermées : elles AGRANDISSENT le catalogue, et
        // pour une boutique fermée cela annulait la sanction.
        //
        // La suppression n'agrandit rien, et deux lectures s'opposent :
        //   - la fermer, parce qu'une boutique SUSPENDUE ne devrait pas pouvoir
        //     effacer des fiches pendant qu'on instruit son dossier ;
        //   - la laisser, parce qu'un vendeur qui range son catalogue après une
        //     fermeture volontaire ne contourne aucune sanction, et que l'enfermer
        //     sur des fiches qu'il ne peut plus ni vendre ni retirer n'a pas de sens.
        //
        // C'est une décision de politique produit, pas un oubli de garde. On ne la
        // prend pas ici : le comportement reste celui d'avant, et la console ne
        // prétend PAS que la suppression est bloquée.
        // ═════════════════════════════════════════════════════════════════════════
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        var offers = await sender.Send(new ListOffersByProductQuery(id), ct);
        if (offers.IsFailure)
        {
            // Fail-CLOSED : sans certitude sur les offres, on ne supprime pas. Servir
            // ce doute comme un feu vert détruirait la fiche et laisserait les offres.
            return Results.Json(
                new { error = "Impossible de vérifier les offres liées à ce produit. Suppression annulée." },
                statusCode: 503);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ON DISTINGUE SES OFFRES DE CELLES DES AUTRES.
        //
        // `ListOffersByProductQuery` renvoie les offres de TOUS les vendeurs sur ce
        // produit. Le vendeur ne peut supprimer que les siennes (`OwnsOfferAsync`
        // refuse le reste) : lui dire « supprimez-les d'abord » quand l'offre
        // bloquante appartient à un tiers, c'est lui demander l'impossible — le
        // défaut même que ce garde-fou prétendait corriger.
        //
        // Le cas ne devrait plus se produire depuis le contrôle de propriété ajouté à
        // la création d'offre, mais des données antérieures peuvent exister. On le
        // traite donc explicitement, avec un message qui dit la vérité.
        // ─────────────────────────────────────────────────────────────────────────
        var mine = offers.Value.Count(o => o.SellerId == seller.Id);
        var others = offers.Value.Count - mine;

        if (mine > 0)
        {
            return Results.Json(
                new
                {
                    error = $"Ce produit porte encore {mine} mise(s) en vente. "
                          + "Supprimez-les d'abord — sinon elles resteraient en vente sans fiche produit.",
                },
                statusCode: 409);
        }

        if (others > 0)
        {
            return Results.Json(
                new
                {
                    error = "Ce produit porte des mises en vente qui ne vous appartiennent pas. "
                          + "Contactez l'assistance : la suppression laisserait ces offres sans fiche produit.",
                },
                statusCode: 409);
        }

        return (await sender.Send(new DeleteProductCommand(id), ct)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Résout le vendeur, vérifie SON DROIT DE VENDRE puis la propriété du produit,
    /// et exécute la commande.
    ///
    /// Cet enchaînement ne sert que des ÉCRITURES. Le contrôle de statut est donc
    /// posé ici plutôt que dans chacune des huit routes qui l'utilisent : recopié
    /// huit fois, il aurait fini par manquer une fois.
    ///
    /// L'ORDRE EST VOULU. Le droit de vendre est vérifié AVANT la propriété : à un
    /// vendeur suspendu, on répond « votre boutique est suspendue » plutôt que
    /// « produit introuvable ». La seconde réponse serait exacte du point de vue de
    /// l'anti-énumération, et parfaitement trompeuse pour l'intéressé, qui irait
    /// chercher un problème dans son catalogue.
    /// </summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ICatalogModuleApi catalog, ISellerModuleApi sellers, Guid productId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller!.Id, productId, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    /// <param name="CategoryId">
    /// Où ranger le produit. NULLE = inchangée, ce qui garde compatibles les clients qui
    /// ne l'envoient pas encore — l'application mobile, notamment.
    ///
    /// CHANGER DE CATÉGORIE RETIRE DE LA VENTE un produit actif, le temps que
    /// l'administration revalide son classement. Voir `Product.ChangeCategory`.
    /// </param>
    public sealed record UpdateProductRequest(
        string Name, string Description, Guid? BrandId, string? Gtin, string? Ean,
        Guid? ProductGroupId, IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags,
        Guid? CategoryId = null);

    public sealed record StatusRequest(string Status);
    /// <summary>
    /// `ExternalId` A ÉTÉ RETIRÉ DE CE CONTRAT. C'était une clé de stockage fournie
    /// par le client, et elle servait de clé de SUPPRESSION : un vendeur pouvait
    /// déclarer l'URL d'une image d'un concurrent, puis la supprimer. Une clé de
    /// stockage ne peut venir que du service qui a écrit l'objet.
    /// </summary>
    public sealed record MediaRequest(string Url, string? Type, string? AltText, bool IsPrimary);
    public sealed record ReorderRequest(IReadOnlyList<Guid> OrderedMediaIds);
    public sealed record VariantRequest(
        string Sku, IReadOnlyDictionary<string, string>? Attributes, string? Barcode, int WeightGrams,
        int? LengthMm, int? WidthMm, int? HeightMm);
}
