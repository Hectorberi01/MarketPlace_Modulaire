using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Reviews.Application.Reviews.Commands;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — avis : consultation des avis et de la note d'un produit, et
/// signalement d'un avis abusif (modération). La réponse publique du vendeur à un
/// avis n'est pas encore une capacité du module Reviews.
/// </summary>
public static class SellerReviewEndpoints
{
    public static IEndpointRouteBuilder MapSellerReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapGroup("/seller/reviews").WithTags("Vendeur · Avis").RequireAuthorization();

        reviews.MapGet("/products/{productId:guid}", ByProductAsync);
        reviews.MapGet("/products/{productId:guid}/rating", RatingAsync);
        reviews.MapPost("/{reviewId:guid}/flag", FlagAsync);

        // Réponse publique du vendeur à un avis : pas de commande dédiée côté
        // module Reviews pour l'instant.
        reviews.MapPost("/{reviewId:guid}/reply", (Guid reviewId) => SellerBff.NotImplemented("reviews.seller_reply"));

        return app;
    }

    /// <summary>Avis d'un produit du vendeur.</summary>
    private static async Task<IResult> ByProductAsync(Guid productId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsByProductQuery(productId), ct)).Match(r => Results.Ok(r));

    /// <summary>Note agrégée d'un produit.</summary>
    private static async Task<IResult> RatingAsync(Guid productId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRatingQuery(productId), ct)).Match(r => Results.Ok(r));

    /// <summary>Signale un avis pour modération.</summary>
    private static async Task<IResult> FlagAsync(Guid reviewId, ISender sender, CancellationToken ct)
        => (await sender.Send(new FlagReviewCommand(reviewId), ct)).Match(() => Results.NoContent());
}
