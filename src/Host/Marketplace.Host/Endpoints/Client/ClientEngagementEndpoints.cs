using MediatR;
using System.Security.Claims;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Loyalty.Application.Accounts;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;

namespace Marketplace.Host.Client.Endpoints;

/// <summary>
/// BFF Client — engagement acheteur : avis produits, suivi des retours, fidélité
/// et litiges. Tout est scopé à l'utilisateur du jeton.
/// </summary>
public static class ClientEngagementEndpoints
{
    public static IEndpointRouteBuilder MapClientEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/client").WithTags("Client · Engagement").RequireAuthorization();

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
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SubmitReviewCommand(
            buyerId, request.ProductId, request.OrderId, request.Rating, request.Title ?? string.Empty, request.Body), ct);
        return result.Match(id => Results.Ok(new { reviewId = id }));
    }

    /// <summary>Liste les demandes de retour de l'utilisateur (avec statut).</summary>
    private static async Task<IResult> ListReturnsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyReturnsQuery(buyerId), ct)).Match(r => Results.Ok(r));

    /// <summary>
    /// Détail d'une demande de retour — RÉSERVÉ À SON AUTEUR.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// `GetReturnQuery` n'est pas scopé : il rend n'importe quel retour à qui en
    /// connaît l'identifiant. Ce contrôle n'était pas fait ici, alors qu'il l'est
    /// partout ailleurs — `ListMyReturnsQuery` filtre par acheteur, et le BFF vendeur
    /// passe par `OwnsReturnAsync`. Un acheteur connecté pouvait donc énumérer les
    /// retours de tous les autres : motif, vendeur, commande, montant remboursé,
    /// suivi transporteur.
    ///
    /// On renvoie 404 plutôt que 403 : répondre « interdit » confirmerait l'existence
    /// du retour, et donc de la commande sous-jacente.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> GetReturnAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetReturnQuery(id), ct);
        if (result.IsFailure || result.Value.BuyerId != userId)
        {
            return Results.NotFound(new { error = "return_not_found" });
        }

        return Results.Ok(result.Value);
    }

    /// <summary>Compte fidélité : solde, palier, historique.</summary>
    private static async Task<IResult> LoyaltyAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyLoyaltyQuery(userId), ct)).Match(l => Results.Ok(l));

    /// <summary>Utilise (échange) des points de fidélité.</summary>
    private static async Task<IResult> RedeemAsync(ClaimsPrincipal user, RedeemRequest request, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RedeemPointsCommand(userId, request.Amount), ct)).Match(() => Results.NoContent());

    /// <summary>Liste les litiges de l'utilisateur.</summary>
    private static async Task<IResult> ListDisputesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyDisputesQuery(userId), ct)).Match(d => Results.Ok(d));

    /// <summary>
    /// Détail d'un litige (fil de messages) — RÉSERVÉ À SON AUTEUR.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Même correction que `GetReturnAsync`, et l'oubli était ici plus frappant :
    /// `ReplyDisputeAsync`, vingt-cinq lignes plus bas, faisait déjà exactement ce
    /// contrôle, commentaire à l'appui. On pouvait donc RÉPONDRE seulement à son
    /// litige, mais LIRE celui de n'importe qui — fil de messages, photos, montant
    /// de remboursement, identifiants de commande et de plaignant.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> GetDisputeAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetDisputeQuery(id), ct);
        if (result.IsFailure || result.Value.RaisedBy != userId)
        {
            return Results.NotFound(new { error = "dispute_not_found" });
        }

        return Results.Ok(result.Value);
    }

    /// <summary>Ouvre un litige sur une commande.</summary>
    private static async Task<IResult> OpenDisputeAsync(ClaimsPrincipal user, OpenDisputeRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
