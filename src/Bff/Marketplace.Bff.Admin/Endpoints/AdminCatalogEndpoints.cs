using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Brands.Commands.CreateBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.DeleteBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.PublishBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.UnpublishBrand;
using Marketplace.Modules.Catalog.Application.Brands.Commands.UpdateBrand;
using Marketplace.Modules.Catalog.Application.Brands.Queries.GetBrand;
using Marketplace.Modules.Catalog.Application.Brands.Queries.ListBrands;
using Marketplace.Modules.Catalog.Application.Categories.Commands.CreateCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.DeleteCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.UnpublishCategory;
using Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory;
using Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategory;
using Marketplace.Modules.Catalog.Application.Categories.Queries.ListCategories;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>BFF Admin — gouvernance du catalogue : taxonomie (catégories) et marques.</summary>
public static class AdminCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapAdminGroup("/admin/categories", "Admin · Catégories");
        categories.MapGet("/", ListCategoriesAsync);
        categories.MapGet("/{id:guid}", GetCategoryAsync);
        categories.MapPost("/", CreateCategoryAsync);
        categories.MapPut("/{id:guid}", UpdateCategoryAsync);
        categories.MapPost("/{id:guid}/publish", PublishCategoryAsync);
        categories.MapPost("/{id:guid}/unpublish", UnpublishCategoryAsync);
        categories.MapDelete("/{id:guid}", DeleteCategoryAsync);

        var brands = app.MapAdminGroup("/admin/brands", "Admin · Marques");
        brands.MapGet("/", ListBrandsAsync);
        brands.MapGet("/{id:guid}", GetBrandAsync);
        brands.MapPost("/", CreateBrandAsync);
        brands.MapPut("/{id:guid}", UpdateBrandAsync);
        brands.MapPost("/{id:guid}/publish", PublishBrandAsync);
        brands.MapPost("/{id:guid}/unpublish", UnpublishBrandAsync);
        brands.MapDelete("/{id:guid}", DeleteBrandAsync);

        return app;
    }

    private static async Task<IResult> ListCategoriesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCategoriesQuery(), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> GetCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCategoryQuery(id), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateCategoryAsync(CategoryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCategoryCommand(request.Name, request.ParentId, request.ImageUrl, request.AttributeSchema), ct)).Match(id => Results.Ok(new { categoryId = id }));

    private static async Task<IResult> UpdateCategoryAsync(Guid id, CategoryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCategoryCommand(id, request.Name, request.ImageUrl, request.AttributeSchema), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> PublishCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishCategoryCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UnpublishCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new UnpublishCategoryCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteCategoryCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListBrandsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListBrandsQuery(), ct)).Match(b => Results.Ok(b));

    private static async Task<IResult> GetBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetBrandQuery(id), ct)).Match(b => Results.Ok(b));

    private static async Task<IResult> CreateBrandAsync(BrandRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateBrandCommand(request.Name, request.LogoUrl, request.Description), ct)).Match(id => Results.Ok(new { brandId = id }));

    private static async Task<IResult> UpdateBrandAsync(Guid id, BrandRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateBrandCommand(id, request.Name, request.LogoUrl, request.Description), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> PublishBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishBrandCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UnpublishBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new UnpublishBrandCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteBrandCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record CategoryRequest(string Name, Guid? ParentId, string? ImageUrl, string? AttributeSchema);
    public sealed record BrandRequest(string Name, string? LogoUrl, string? Description);
}
