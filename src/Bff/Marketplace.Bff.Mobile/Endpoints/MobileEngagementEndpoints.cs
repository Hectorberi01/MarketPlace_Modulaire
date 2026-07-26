using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Loyalty.Application.Accounts;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — engagement acheteur : avis produits, suivi des retours, fidélité
/// et litiges. Tout est scopé à l'utilisateur du jeton.
/// </summary>
public static class MobileEngagementEndpoints
{
    public static IEndpointRouteBuilder MapMobileEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/mobile").WithTags("Mobile · Engagement").RequireAuthorization();

        // Avis (dépôt ; la lecture par produit existe déjà côté catalogue).
        grp.MapPost("/reviews", SubmitReviewAsync);

        // Retours (la demande existe déjà sur /orders ; ici le suivi).
        grp.MapGet("/returns", ListReturnsAsync);
        grp.MapGet("/returns/{id:guid}", GetReturnAsync);

        // Fidélité.
        grp.MapGet("/loyalty", LoyaltyAsync);
        grp.MapPost("/loyalty/redeem", RedeemAsync);

        // Litiges.
        grp.MapGet("/disputes", ListDisputesAsync);
        grp.MapGet("/disputes/{id:guid}", GetDisputeAsync);
        grp.MapPost("/disputes", OpenDisputeAsync);
        grp.MapPost("/disputes/{id:guid}/messages", ReplyDisputeAsync);

        return app;
    }

    /// <summary>Dépose un avis vérifié sur un produit acheté (commande confirmée).</summary>
    private static async Task<IResult> SubmitReviewAsync(ClaimsPrincipal user, SubmitReviewRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SubmitReviewCommand(
            buyerId, request.ProductId, request.OrderId, request.Rating, request.Title ?? string.Empty, request.Body), ct);
        return result.Match(id => Results.Ok(new { reviewId = id }));
    }

    /// <summary>Liste les demandes de retour de l'utilisateur (avec statut).</summary>
    private static async Task<IResult> ListReturnsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyReturnsQuery(buyerId), ct)).Match(r => Results.Ok(r));

    /// <summary>Détail d'une demande de retour.</summary>
    private static async Task<IResult> GetReturnAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is null
            ? Results.Unauthorized()
            : (await sender.Send(new GetReturnQuery(id), ct)).Match(r => Results.Ok(r));

    /// <summary>Compte fidélité : solde, palier, historique.</summary>
    private static async Task<IResult> LoyaltyAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyLoyaltyQuery(userId), ct)).Match(l => Results.Ok(l));

    /// <summary>Utilise (échange) des points de fidélité.</summary>
    private static async Task<IResult> RedeemAsync(ClaimsPrincipal user, RedeemRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RedeemPointsCommand(userId, request.Amount), ct)).Match(() => Results.NoContent());

    /// <summary>Liste les litiges de l'utilisateur.</summary>
    private static async Task<IResult> ListDisputesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyDisputesQuery(userId), ct)).Match(d => Results.Ok(d));

    /// <summary>Détail d'un litige (fil de messages).</summary>
    private static async Task<IResult> GetDisputeAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is null
            ? Results.Unauthorized()
            : (await sender.Send(new GetDisputeQuery(id), ct)).Match(d => Results.Ok(d));

    /// <summary>Ouvre un litige sur une commande.</summary>
    private static async Task<IResult> OpenDisputeAsync(ClaimsPrincipal user, OpenDisputeRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new OpenDisputeCommand(
            userId, request.OrderId, request.Type, request.Message, request.PhotoUrl), ct);
        return result.Match(id => Results.Ok(new { disputeId = id }));
    }

    /// <summary>Répond à un litige (message de l'acheteur dans le fil).</summary>
    private static async Task<IResult> ReplyDisputeAsync(ClaimsPrincipal user, Guid id, DisputeReplyRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        // Sécurité : on ne répond qu'à SON propre litige (RaisedBy == acheteur).
        var dispute = await sender.Send(new GetDisputeQuery(id), ct);
        if (dispute.IsFailure || dispute.Value.RaisedBy != userId)
        {
            return Results.NotFound(new { error = "dispute_not_found" });
        }

        return (await sender.Send(new AddDisputeMessageCommand(id, userId, request.Message, request.PhotoUrl), ct))
            .Match(() => Results.NoContent());
    }

    public sealed record SubmitReviewRequest(Guid ProductId, Guid OrderId, int Rating, string? Title, string Body);
    public sealed record RedeemRequest(int Amount);
    public sealed record OpenDisputeRequest(Guid OrderId, string Type, string Message, string? PhotoUrl);
    public sealed record DisputeReplyRequest(string Message, string? PhotoUrl);
}
