using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
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

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — catalogue. Liste/lecture scopées au vendeur du jeton ; chaque
/// mutation par id vérifie que le produit appartient au vendeur (anti-IDOR).
/// </summary>
public static class SellerCatalogEndpoints
{
    public static IEndpointRouteBuilder MapSellerCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/seller/products").WithTags("Vendeur · Catalogue").RequireAuthorization();

        products.MapGet("/", ListMineAsync);
        products.MapGet("/{id:guid}", GetAsync);
        // Création produit AVEC upload d'images (multipart/form-data).
        products.MapPost("/", CreateWithImagesAsync).DisableAntiforgery();
        // Traitement d'une image (détourage IA + fond blanc) AVANT création, pour
        // que le vendeur valide le rendu. Renvoie l'image traitée (JPEG).
        products.MapPost("/media/process", ProcessMediaAsync).DisableAntiforgery();
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
        var categories = app.MapGroup("/seller/categories").WithTags("Vendeur · Catalogue").RequireAuthorization();
        categories.MapGet("/", ListCategoriesAsync);

        // Marques de la plateforme (pour le sélecteur de création produit).
        var brands = app.MapGroup("/seller/brands").WithTags("Vendeur · Catalogue").RequireAuthorization();
        brands.MapGet("/", ListBrandsAsync);

        return app;
    }

    /// <summary>Catégories de la plateforme (lecture seule) pour alimenter le formulaire produit.</summary>
    private static async Task<IResult> ListCategoriesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCategoriesQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>Marques de la plateforme (lecture seule) pour alimenter le formulaire produit.</summary>
    private static async Task<IResult> ListBrandsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListBrandsQuery(), ct)).Match(b => Results.Ok(b));

    /// <summary>Produits de la boutique du jeton (l'id vendeur est déduit du token).</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListProductsBySellerQuery(seller.Id), ct)).Match(p => Results.Ok(p));
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
            if (UploadValidation.ValidateImage(file) is { } invalid)
            {
                return invalid;
            }
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            uploads.Add(new ProductImageUpload(file.FileName, file.ContentType, ms.ToArray()));
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

        if (UploadValidation.ValidateImage(image) is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);

        var result = await sender.Send(
            new ProcessImageCommand(image.FileName, image.ContentType, ms.ToArray()), ct);

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

        if (UploadValidation.ValidateImage(file) is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return (await sender.Send(new UploadProductImageCommand(
                id, file.FileName, file.ContentType, ms.ToArray(), alt, isPrimary), ct))
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

    private static async Task<IResult> DeleteAsync(ClaimsPrincipal user, Guid id, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, catalog, sellers, id, ct,
            () => sender.Send(new DeleteProductCommand(id), ct));

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
