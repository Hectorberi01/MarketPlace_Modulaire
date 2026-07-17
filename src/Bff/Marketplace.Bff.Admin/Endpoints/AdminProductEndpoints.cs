using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;
using Marketplace.Modules.Catalog.Application.Products.Queries.ListAllProducts;
using Marketplace.Modules.Offers.Contracts;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — produits de la plateforme : liste globale et modération
/// (valider → Active, rejeter → Archived) des fiches publiées par les vendeurs.
/// </summary>
public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapAdminGroup("/admin/products", "Admin · Produits");

        products.MapGet("/", ListAsync);
        products.MapGet("/{id:guid}", GetAsync);
        products.MapGet("/{id:guid}/offers", GetOffersAsync);
        products.MapPost("/{id:guid}/approve", ApproveAsync);
        products.MapPost("/{id:guid}/reject", RejectAsync);
        products.MapPatch("/{id:guid}/status", ChangeStatusAsync);

        return app;
    }

    /// <summary>Page de produits (pagination, recherche par nom, filtre statut).</summary>
    private static async Task<IResult> ListAsync(
        ISender sender, CancellationToken ct,
        int page = 1, int pageSize = 20, string? search = null, string? status = null, string? sort = null, string? dir = null)
        => (await sender.Send(new ListAllProductsQuery(page, pageSize, search, status, sort, dir), ct)).Match(p => Results.Ok(p));

    /// <summary>Détail d'un produit (fiche + médias + variantes).</summary>
    private static async Task<IResult> GetAsync(Guid id, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(id, ct);
        return product is null ? Results.NotFound() : Results.Ok(product);
    }

    /// <summary>Offres (tarification) rattachées à un produit : prix, breakdown commission/frais, statut.</summary>
    private static async Task<IResult> GetOffersAsync(Guid id, IOfferModuleApi offers, CancellationToken ct)
        => Results.Ok(await offers.GetOffersByProductAsync(id, ct));

    /// <summary>Valide un produit (le publie : statut Active).</summary>
    private static async Task<IResult> ApproveAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, "Active"), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette un produit (le retire : statut Archived).</summary>
    private static async Task<IResult> RejectAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, "Archived"), ct)).Match(() => Results.NoContent());

    /// <summary>Change librement le statut (Active / Archived / Draft).</summary>
    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, request.Status), ct)).Match(() => Results.NoContent());

    public sealed record StatusRequest(string Status);
}
