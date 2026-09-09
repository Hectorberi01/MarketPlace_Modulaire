using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Reviews.Application.Reviews.Commands;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — avis. Restreint aux produits de la boutique du jeton ; le
/// signalement vérifie que l'avis cible un produit du vendeur (anti-IDOR). La
/// réponse publique à un avis n'est pas encore une capacité du module Reviews.
///
/// LE CONTRÔLE DE PROPRIÉTÉ NE SUFFISAIT PAS À RENDRE LE SIGNALEMENT INOFFENSIF :
/// tant que `Flagged` retirait l'avis des lectures publiques, `OwnsReviewAsync`
/// autorisait précisément ce qu'il fallait interdire — un vendeur effaçant les
/// mauvais avis de ses propres produits. Le signalement ne dépublie plus (voir
/// `ReviewRepository.PubliclyVisible`) ; le vendeur alerte la modération, il ne la
/// remplace pas.
/// </summary>
public static class SellerReviewEndpoints
{
    public static IEndpointRouteBuilder MapSellerReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapSellerGroup("/seller/reviews", "Vendeur · Avis");

        reviews.MapGet("/", ListMineAsync);
        reviews.MapGet("/products/{productId:guid}", ByProductAsync);
        reviews.MapGet("/products/{productId:guid}/rating", RatingAsync);
        reviews.MapPost("/{reviewId:guid}/flag", FlagAsync);
        reviews.MapPost("/{reviewId:guid}/reply", ReplyAsync);

        return app;
    }

    /// <summary>Tous les avis clients ciblant les produits de la boutique du jeton.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListReviewsBySellerQuery(seller.Id), ct)).Match(r => Results.Ok(r));
    }

    /// <summary>Avis d'un produit du vendeur (404 si le produit ne lui appartient pas).</summary>
    private static async Task<IResult> ByProductAsync(ClaimsPrincipal user, Guid productId, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
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

        return (await sender.Send(new ListReviewsByProductQuery(productId), ct)).Match(r => Results.Ok(r));
    }

    /// <summary>Note agrégée d'un produit du vendeur.</summary>
    private static async Task<IResult> RatingAsync(ClaimsPrincipal user, Guid productId, ISender sender, ICatalogModuleApi catalog, ISellerModuleApi sellers, CancellationToken ct)
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

        return (await sender.Send(new GetProductRatingQuery(productId), ct)).Match(r => Results.Ok(r));
    }

    /// <summary>Signale un avis ciblant un produit du vendeur.</summary>
    private static async Task<IResult> FlagAsync(ClaimsPrincipal user, Guid reviewId, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsReviewAsync(sender, seller.Id, reviewId, ct))
        {
            return SellerBff.NotFoundOrForbidden("review");
        }

        return (await sender.Send(new FlagReviewCommand(reviewId), ct)).Match(() => Results.NoContent());
    }

    /// <summary>Réponse publique du vendeur à un avis ciblant un de ses produits.</summary>
    private static async Task<IResult> ReplyAsync(ClaimsPrincipal user, Guid reviewId, ReplyRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsReviewAsync(sender, seller.Id, reviewId, ct))
        {
            return SellerBff.NotFoundOrForbidden("review");
        }

        return (await sender.Send(new ReplyToReviewCommand(reviewId, request.Body), ct)).Match(() => Results.NoContent());
    }

    public sealed record ReplyRequest(string Body);
}
