using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;
using Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;
using Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — catalogue : création et entretien des fiches produits du vendeur
/// (brouillon → publication), médias et statut. Le SellerId est imposé par le
/// jeton à la création.
/// </summary>
public static class SellerCatalogEndpoints
{
    public static IEndpointRouteBuilder MapSellerCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/seller/products").WithTags("Vendeur · Catalogue").RequireAuthorization();

        products.MapGet("/{id:guid}", GetAsync);
        products.MapPost("/", CreateAsync);
        products.MapPut("/{id:guid}", UpdateAsync);
        products.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        products.MapPost("/{id:guid}/media", AddMediaAsync);

        return app;
    }

    /// <summary>Fiche produit (vue d'édition).</summary>
    private static async Task<IResult> GetAsync(Guid id, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(id, ct);
        return product is null ? Results.NotFound(new { error = "product_not_found" }) : Results.Ok(product);
    }

    /// <summary>Crée une fiche produit (Draft) pour la boutique du jeton.</summary>
    private static async Task<IResult> CreateAsync(ClaimsPrincipal user, CreateProductRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new CreateProductCommand(
            seller.Id, request.CategoryId, request.Name, request.Description,
            request.BrandId, request.Gtin, request.Ean, request.ProductGroupId,
            request.Attributes, request.Tags), ct);

        return result.Match(id => Results.Ok(new { productId = id }));
    }

    /// <summary>Met à jour les champs descriptifs d'un produit.</summary>
    private static async Task<IResult> UpdateAsync(Guid id, UpdateProductRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateProductCommand(
                id, request.Name, request.Description, request.BrandId, request.Gtin,
                request.Ean, request.ProductGroupId, request.Attributes, request.Tags), ct))
            .Match(() => Results.NoContent());

    /// <summary>Publie / archive / repasse en brouillon un produit (Active, Archived, Draft).</summary>
    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, request.Status), ct)).Match(() => Results.NoContent());

    /// <summary>Ajoute un média (image/vidéo) à la fiche produit.</summary>
    private static async Task<IResult> AddMediaAsync(Guid id, MediaRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddProductMediaCommand(
                id, request.Url, request.Type ?? "Image", request.AltText, request.IsPrimary, request.ExternalId), ct))
            .Match(mediaId => Results.Ok(new { mediaId }));

    public sealed record CreateProductRequest(
        Guid CategoryId, string Name, string Description, Guid? BrandId, string? Gtin, string? Ean,
        Guid? ProductGroupId, IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags);

    public sealed record UpdateProductRequest(
        string Name, string Description, Guid? BrandId, string? Gtin, string? Ean,
        Guid? ProductGroupId, IReadOnlyDictionary<string, string>? Attributes, IReadOnlyList<string>? Tags);

    public sealed record StatusRequest(string Status);
    public sealed record MediaRequest(string Url, string? Type, string? AltText, bool IsPrimary, string? ExternalId);
}
