using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Catalog.Application.Brands.Commands.CreateBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.DeleteBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.PublishBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.UpdateBrand;
using Marketplace.Modules.Catalog.Application.Brands.Queries.GetBrand;
using Marketplace.Modules.Catalog.Application.Categories.Commands.CreateCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.DeleteCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory;
using Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategory;
using Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategoryFormPreview;
using Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;
using Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;
using Marketplace.Modules.Catalog.Application.Products.Commands.DeleteProduct;
using Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;
using Marketplace.Modules.Catalog.Application.Products.Queries.GetProduct;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Catalog. Chaque route ne fait que traduire HTTP
/// &lt;-&gt; message CQRS ; toute la logique vit dans les handlers. (À terme, ces
/// routes passeront par un BFF ; ici on expose l'API applicative directement.)
/// </summary>
public static class CatalogEndpoints
{
    /// <summary>Enregistre l'ensemble des routes du catalogue (produits, marques, catégories).</summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        MapProducts(app);
        MapBrands(app);
        MapCategories(app);
        return app;
    }

    // Products
    private static void MapProducts(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/catalog/products").WithTags("Catalog · Products");

        group.MapPost("/", CreateProductAsync).WithName("CreateProduct");
        group.MapGet("/{id:guid}", GetProductAsync).WithName("GetProduct");
        group.MapPut("/{id:guid}", UpdateProductAsync).WithName("UpdateProduct");
        group.MapDelete("/{id:guid}", DeleteProductAsync).WithName("DeleteProduct");
        group.MapPatch("/{id:guid}/status", ChangeProductStatusAsync).WithName("ChangeProductStatus");
        group.MapPost("/{id:guid}/variants", AddProductVariantAsync).WithName("AddProductVariant");
        group.MapPost("/{id:guid}/media", AddProductMediaAsync).WithName("AddProductMedia");
    }

    /// <summary>Crée un produit (statut Draft) et renvoie son identifiant.</summary>
    private static async Task<IResult> CreateProductAsync(CreateProductRequest request, ISender sender, CancellationToken ct)
    {
        var command = new CreateProductCommand(
            request.SellerId, request.CategoryId, request.Name, request.Description,
            request.BrandId, request.Gtin, request.Ean, request.ProductGroupId, request.Attributes, request.Tags);

        var result = await sender.Send(command, ct);
        return result.Match(id => Results.Created($"/api/catalog/products/{id}", new { id }));
    }

    /// <summary>Récupère un produit avec ses variantes et ses médias.</summary>
    private static async Task<IResult> GetProductAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetProductQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour les champs descriptifs d'un produit (nom, marque, attributs, tags…).</summary>
    private static async Task<IResult> UpdateProductAsync(Guid id, UpdateProductRequest request, ISender sender, CancellationToken ct)
    {
        var command = new UpdateProductCommand(
            id, request.Name, request.Description, request.BrandId, request.Gtin,
            request.Ean, request.ProductGroupId, request.Attributes, request.Tags);

        var result = await sender.Send(command, ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime définitivement un produit (variantes et médias inclus par cascade).</summary>
    private static async Task<IResult> DeleteProductAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteProductCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Change le statut d'un produit : Active (publier), Archived (retirer) ou Draft.</summary>
    private static async Task<IResult> ChangeProductStatusAsync(Guid id, ChangeStatusRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ChangeProductStatusCommand(id, request.Status), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Ajoute une variante (SKU, attributs, dimensions) à un produit.</summary>
    private static async Task<IResult> AddProductVariantAsync(Guid id, AddVariantRequest request, ISender sender, CancellationToken ct)
    {
        var command = new AddProductVariantCommand(
            id, request.Sku, request.Attributes, request.Barcode, request.WeightGrams,
            request.LengthMm, request.WidthMm, request.HeightMm);

        var result = await sender.Send(command, ct);
        return result.Match(variantId => Results.Created($"/api/catalog/products/{id}", new { variantId }));
    }

    /// <summary>Ajoute un média (image ou vidéo) à un produit.</summary>
    private static async Task<IResult> AddProductMediaAsync(Guid id, AddMediaRequest request, ISender sender, CancellationToken ct)
    {
        var command = new AddProductMediaCommand(id, request.Url, request.Type, request.AltText, request.IsPrimary);
        var result = await sender.Send(command, ct);
        return result.Match(mediaId => Results.Created($"/api/catalog/products/{id}", new { mediaId }));
    }

    // ------------------------------------------------------------------ Brands

    private static void MapBrands(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/catalog/brands").WithTags("Catalog · Brands");

        group.MapPost("/", CreateBrandAsync).WithName("CreateBrand");
        group.MapGet("/{id:guid}", GetBrandAsync).WithName("GetBrand");
        group.MapPut("/{id:guid}", UpdateBrandAsync).WithName("UpdateBrand");
        group.MapDelete("/{id:guid}", DeleteBrandAsync).WithName("DeleteBrand");
        group.MapPost("/{id:guid}/publish", PublishBrandAsync).WithName("PublishBrand");
    }

    /// <summary>Crée une marque (statut Pending, en attente de modération).</summary>
    private static async Task<IResult> CreateBrandAsync(CreateBrandRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateBrandCommand(request.Name, request.LogoUrl, request.Description), ct);
        return result.Match(id => Results.Created($"/api/catalog/brands/{id}", new { id }));
    }

    /// <summary>Récupère une marque par son identifiant.</summary>
    private static async Task<IResult> GetBrandAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetBrandQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour le nom, le logo et la description d'une marque.</summary>
    private static async Task<IResult> UpdateBrandAsync(Guid id, UpdateBrandRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateBrandCommand(id, request.Name, request.LogoUrl, request.Description), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime une marque (référence souple : les produits ne sont pas modifiés).</summary>
    private static async Task<IResult> DeleteBrandAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteBrandCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Publie (approuve) une marque : Pending -> Active.</summary>
    private static async Task<IResult> PublishBrandAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new PublishBrandCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    // -------------------------------------------------------------- Categories

    private static void MapCategories(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/catalog/categories").WithTags("Catalog · Categories");

        group.MapPost("/", CreateCategoryAsync).WithName("CreateCategory");
        group.MapGet("/{id:guid}", GetCategoryAsync).WithName("GetCategory");
        group.MapPut("/{id:guid}", UpdateCategoryAsync).WithName("UpdateCategory");
        group.MapDelete("/{id:guid}", DeleteCategoryAsync).WithName("DeleteCategory");
        group.MapPost("/{id:guid}/publish", PublishCategoryAsync).WithName("PublishCategory");

        // Aperçu du formulaire produit engendré par la branche — voir le handler.
        group.MapGet("/{id:guid}/form-preview", PreviewCategoryFormAsync).WithName("PreviewCategoryForm");
    }

    /// <summary>
    /// Aperçu du formulaire produit qu'engendre cette catégorie, héritage compris.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// L'ADMINISTRATEUR NE VOYAIT JAMAIS CE QU'IL IMPOSAIT.
    ///
    /// Poser un attribut sur « Informatique » le fait apparaître dans le formulaire de
    /// toutes les feuilles dessous, mêlé à ceux des étages intermédiaires. Depuis
    /// l'écran où on le pose, rien ne montre ce résultat composé : il fallait ouvrir la
    /// console vendeur avec un compte de test pour le découvrir — quand on y pensait.
    ///
    /// La réponse est le MODÈLE du formulaire, groupé par étage d'origine. La console
    /// d'administration le passe au même composant que la console vendeur : un seul
    /// rendu, donc un aperçu qui ne peut pas diverger de la réalité.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> PreviewCategoryFormAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetCategoryFormPreviewQuery(id), ct);
        return result.Match(preview => Results.Ok(preview));
    }

    /// <summary>Crée une catégorie (statut Draft), éventuellement rattachée à un parent.</summary>
    private static async Task<IResult> CreateCategoryAsync(CreateCategoryRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new CreateCategoryCommand(request.Name, request.ParentId, request.ImageUrl, request.AttributeSchema), ct);
        return result.Match(id => Results.Created($"/api/catalog/categories/{id}", new { id }));
    }

    /// <summary>Récupère une catégorie par son identifiant.</summary>
    private static async Task<IResult> GetCategoryAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetCategoryQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour le nom (slug/chemin recalculés), l'image et le schéma d'attributs d'une catégorie.</summary>
    private static async Task<IResult> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateCategoryCommand(id, request.Name, request.ImageUrl, request.AttributeSchema), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime une catégorie (référence souple : descendants et produits non répercutés).</summary>
    private static async Task<IResult> DeleteCategoryAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteCategoryCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Publie une catégorie : Draft -> Published (visible dans l'arbre).
    ///
    /// `includeDescendants` publie aussi toute la descendance. La commande renvoie
    /// désormais le nombre de catégories basculées — il peut être inférieur à
    /// l'attendu si des sous-catégories sont archivées, celles-ci étant ignorées.
    /// </summary>
    private static async Task<IResult> PublishCategoryAsync(
        Guid id, bool includeDescendants, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new PublishCategoryCommand(id, includeDescendants), ct);
        return result.Match(count => Results.Ok(new { published = count }));
    }

    // ------------------------------------------------------- Contrats d'entrée

    public sealed record CreateProductRequest(
        Guid SellerId, Guid CategoryId, string Name, string Description,
        Guid? BrandId, string? Gtin, string? Ean, Guid? ProductGroupId,
        IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags);

    public sealed record UpdateProductRequest(
        string Name, string Description, Guid? BrandId, string? Gtin, string? Ean,
        Guid? ProductGroupId, IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags);

    public sealed record ChangeStatusRequest(string Status);

    public sealed record AddVariantRequest(
        string Sku, IReadOnlyDictionary<string, string>? Attributes, string? Barcode,
        int WeightGrams, int? LengthMm, int? WidthMm, int? HeightMm);

    /// <summary>`ExternalId` retiré : voir <c>AddProductMediaCommand</c>. Une clé de
    /// stockage fournie par l'appelant permettait de supprimer l'objet d'autrui.</summary>
    public sealed record AddMediaRequest(string Url, string Type, string? AltText, bool IsPrimary);
    public sealed record CreateBrandRequest(string Name, string? LogoUrl, string? Description);
    public sealed record UpdateBrandRequest(string Name, string? LogoUrl, string? Description);
    public sealed record CreateCategoryRequest(string Name, Guid? ParentId, string? ImageUrl, string? AttributeSchema);
    public sealed record UpdateCategoryRequest(string Name, string? ImageUrl, string? AttributeSchema);
}
