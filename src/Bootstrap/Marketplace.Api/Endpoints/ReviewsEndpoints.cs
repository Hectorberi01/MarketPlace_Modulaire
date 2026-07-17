using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Reviews.Application.Reviews.Commands;
using Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Reviews : dépôt d'avis « achat vérifié », lecture
/// publique des avis et de la note produit, et modération.
/// </summary>
public static class ReviewsEndpoints
{
    /// <summary>Enregistre les routes des avis.</summary>
    public static IEndpointRouteBuilder MapReviewsEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapAuthenticatedGroup("/api/reviews").WithTags("Reviews");

        reviews.MapPost("/", SubmitAsync).WithName("SubmitReview").RequireAuthorization();
        reviews.MapGet("/{id:guid}", GetAsync).WithName("GetReview").AllowAnonymous();
        reviews.MapGet("/product/{productId:guid}", ListByProductAsync).WithName("ListReviewsByProduct").AllowAnonymous();
        reviews.MapGet("/product/{productId:guid}/rating", GetRatingAsync).WithName("GetProductRating").AllowAnonymous();

        reviews.MapPost("/{id:guid}/flag", FlagAsync).WithName("FlagReview").RequireAuthorization();
        reviews.MapPost("/{id:guid}/reject", RejectAsync).WithName("RejectReview").RequireAuthorization(p => p.RequireRole("Admin", "Moderator"));
        reviews.MapPost("/{id:guid}/restore", RestoreAsync).WithName("RestoreReview").RequireAuthorization(p => p.RequireRole("Admin", "Moderator"));

        return app;
    }

    /// <summary>Dépose un avis sur un produit d'une commande confirmée de l'acheteur connecté.</summary>
    private static async Task<IResult> SubmitAsync(SubmitRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(
            new SubmitReviewCommand(buyerId, request.ProductId, request.OrderId, request.Rating, request.Title ?? string.Empty, request.Body), ct);
        return result.Match(id => Results.Created($"/api/reviews/{id}", new { id }));
    }

    /// <summary>Récupère un avis par son identifiant.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetReviewQuery(id), ct);
        return result.Match(review => Results.Ok(review));
    }

    /// <summary>Liste les avis publiés d'un produit.</summary>
    private static async Task<IResult> ListByProductAsync(Guid productId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListReviewsByProductQuery(productId), ct);
        return result.Match(reviews => Results.Ok(reviews));
    }

    /// <summary>Renvoie la note agrégée d'un produit (moyenne + nombre d'avis).</summary>
    private static async Task<IResult> GetRatingAsync(Guid productId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetProductRatingQuery(productId), ct);
        return result.Match(rating => Results.Ok(rating));
    }

    /// <summary>Signale un avis (modération).</summary>
    private static async Task<IResult> FlagAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new FlagReviewCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Rejette un avis (Admin / Modérateur).</summary>
    private static async Task<IResult> RejectAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RejectReviewCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Restaure un avis signalé / rejeté (Admin / Modérateur).</summary>
    private static async Task<IResult> RestoreAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RestoreReviewCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record SubmitRequest(Guid ProductId, Guid OrderId, int Rating, string? Title, string Body);
}
