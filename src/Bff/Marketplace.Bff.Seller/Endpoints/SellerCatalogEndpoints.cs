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
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
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
            seller.Id, categoryId, name, description,
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
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var check = await UploadValidation.CheckImageAsync(image, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);

        // ⚠️ Ce contenu part vers un PROCESSEUR D'IMAGE (Cloudinary). Livrer des octets
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
                request.Ean, request.ProductGroupId, request.Attributes, request.Tags), ct));

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
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await sender.Send(new AddProductMediaCommand(
                id, request.Url, request.Type ?? "Image", request.AltText, request.IsPrimary, request.ExternalId), ct))
            .Match(mediaId => Results.Ok(new { mediaId }));
    }

    /// <summary>Téléverse une image (multipart, champ « file ») et l'attache au produit. Scopé vendeur.</summary>
    private static async Task<IResult> UploadMediaAsync(
        ClaimsPrincipal user, Guid id, IFormFile file, ISender sender,
        ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct,
        [FromForm] string? alt = null, [FromForm] bool isPrimary = false)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, id, ct))
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
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, id, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await sender.Send(new AddProductVariantCommand(
                id, request.Sku, request.Attributes, request.Barcode, request.WeightGrams,
                request.LengthMm, request.WidthMm, request.HeightMm), ct))
            .Match(variantId => Results.Ok(new { variantId }));
    }

    /// <summary>Met à jour une variante du produit. Scopé vendeur.</summary>
    private static async Task<IResult> UpdateVariantAsync(ClaimsPrincipal user, Guid id, Guid variantId, VariantRequest request, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new UpdateProductVariantCommand(id, variantId, request.Sku, request.Attributes, request.Barcode, request.WeightGrams), ct));

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

    /// <summary>Résout le vendeur, vérifie la propriété du produit, puis exécute la commande.</summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ICatalogModuleApi catalog, ISellerModuleApi sellers, Guid productId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsProductAsync(catalog, seller.Id, productId, ct))
        {
            return SellerBff.NotFoundOrForbidden("product");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    public sealed record UpdateProductRequest(
        string Name, string Description, Guid? BrandId, string? Gtin, string? Ean,
        Guid? ProductGroupId, IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags);

    public sealed record StatusRequest(string Status);
    public sealed record MediaRequest(string Url, string? Type, string? AltText, bool IsPrimary, string? ExternalId);
    public sealed record ReorderRequest(IReadOnlyList<Guid> OrderedMediaIds);
    public sealed record VariantRequest(
        string Sku, IReadOnlyDictionary<string, string>? Attributes, string? Barcode, int WeightGrams,
        int? LengthMm, int? WidthMm, int? HeightMm);
}
