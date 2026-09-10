using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;
using Marketplace.Modules.Catalog.Application.Products.Commands.SetProductTags;
using Marketplace.Modules.Catalog.Application.Products.Queries.ListAllProducts;
using Marketplace.Modules.Offers.Contracts;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — produits de la plateforme : liste globale et modération des fiches
/// publiées par les vendeurs : valider (-> Active), rejeter (-> Draft, corrigeable),
/// et retirer du catalogue (-> Archived, par PATCH /status).
/// </summary>
public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapAdminGroup("/admin/products", "Admin · Produits");

        products.MapGet("/", ListAsync);
        products.MapGet("/count", CountAsync);
        products.MapGet("/{id:guid}", GetAsync);
        products.MapGet("/{id:guid}/offers", GetOffersAsync);
        products.MapPost("/{id:guid}/approve", ApproveAsync);
        products.MapPost("/{id:guid}/reject", RejectAsync);
        products.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        products.MapPut("/{id:guid}/tags", SetTagsAsync);

        return app;
    }

    /// <summary>Page de produits (pagination, recherche par nom, filtre statut).</summary>
    private static async Task<IResult> ListAsync(
        ISender sender, CancellationToken ct,
        int page = 1, int pageSize = 20, string? search = null, string? status = null, string? sort = null, string? dir = null)
        => (await sender.Send(new ListAllProductsQuery(page, pageSize, search, status, sort, dir), ct)).Match(p => Results.Ok(p));

    /// <summary>
    /// Décompte du catalogue, ventilé par statut.
    ///
    /// Existe pour que le tableau de bord cesse de demander « une page d'un produit »
    /// à la route paginée : deux balayages de la table et un tri complet, pour lire un
    /// entier et jeter la ligne rendue.
    /// </summary>
    private static async Task<IResult> CountAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new CountProductsQuery(), ct)).Match(c => Results.Ok(c));

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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// REJETTE UN PRODUIT : RETOUR EN BROUILLON, PAS ARCHIVAGE.
    ///
    /// Le rejet écrivait `Archived`, et `Activate` refuse explicitement un produit
    /// archivé. Le geste naturel du modérateur — REJETER, le vendeur corrige,
    /// APPROUVER — renvoyait donc un 409, sans qu'aucun code ni message n'indique
    /// que la sortie passe par un retour en brouillon. Un produit rejeté était, en
    /// pratique, un produit perdu.
    ///
    /// « Rejeter » veut dire « pas publiable EN L'ÉTAT », pas « retirer du
    /// catalogue ». Le brouillon est exactement cet état : le vendeur peut éditer sa
    /// fiche, il ne peut toujours pas la publier lui-même (la publication reste
    /// réservée à la modération), et l'index l'a masquée.
    ///
    /// Le retrait ferme reste disponible par `PATCH /status` avec `Archived`, qui
    /// dit ce qu'il fait.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> RejectAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, "Draft"), ct)).Match(() => Results.NoContent());

    /// <summary>Change librement le statut (Active / Archived / Draft).</summary>
    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ChangeProductStatusCommand(id, request.Status), ct)).Match(() => Results.NoContent());

    /// <summary>Remplace la liste des tags du produit (curation : « featured »…).</summary>
    private static async Task<IResult> SetTagsAsync(Guid id, TagsRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetProductTagsCommand(id, request.Tags ?? Array.Empty<string>()), ct)).Match(() => Results.NoContent());

    public sealed record StatusRequest(string Status);
    public sealed record TagsRequest(IReadOnlyList<string>? Tags);
}
