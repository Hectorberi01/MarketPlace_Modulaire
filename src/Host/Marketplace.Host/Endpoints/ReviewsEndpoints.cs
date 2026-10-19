using System.Security.Claims;
using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Reviews.Application.Reviews.Commands;
using Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Host.Endpoints;

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

        // ════════════════════════════════════════════════════════════════════════
        // `/flag` RESTE OUVERT À TOUT COMPTE AUTHENTIFIÉ — MAIS IL NE DÉPUBLIE PLUS.
        //
        // Cette route n'exigeait aucun rôle, là où ses deux voisines `/reject` et
        // `/restore` exigent Admin/Modérateur, et `FlagReviewCommandHandler` ne
        // contrôlait rien non plus. Comme `Review.Flag()` posait `Flagged` et que les
        // trois lectures publiques ne gardaient que `Published`, l'écart de garde se
        // payait cash : un compte créé la minute d'avant faisait tomber à zéro la note
        // d'un produit ou d'une boutique, avis par avis.
        //
        // ALIGNER SUR LES VOISINES AURAIT ÉTÉ LE MAUVAIS RÉFLEXE. Signaler n'est pas
        // modérer : c'est le geste par lequel un acheteur remonte un avis injurieux ou
        // frauduleux. Le réserver aux modérateurs supprimerait le seul canal de
        // remontée existant, pour corriger un défaut qui n'est pas ici.
        //
        // Le défaut était que le signalement AGISSAIT comme une sanction. Il a été
        // corrigé à la source : `Flagged` marque désormais l'avis pour revue sans le
        // retirer des lectures publiques (voir `ReviewRepository.PubliclyVisible`).
        // Le geste redevenu inoffensif, il peut rester ouvert — et le retrait, lui,
        // reste derrière `/reject`, sous rôle.
        // ════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Signale un avis : le marque pour revue de modération. NE LE DÉPUBLIE PAS — voir
    /// l'encadré sur la route.
    /// </summary>
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

    /// <summary>Lève le signalement ou le rejet d'un avis (Admin / Modérateur).</summary>
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
