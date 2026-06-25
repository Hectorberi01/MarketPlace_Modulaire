using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Reviews.Application.Reviews.Commands;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — modération : avis (signaler / rejeter / restaurer) et litiges
/// (revue, résolution, escalade). Accessible aux rôles Admin et Moderator.
/// </summary>
public static class AdminModerationEndpoints
{
    public static IEndpointRouteBuilder MapAdminModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapModerationGroup("/admin/reviews", "Admin · Modération avis");
        reviews.MapGet("/products/{productId:guid}", ReviewsByProductAsync);
        reviews.MapPost("/{id:guid}/flag", FlagAsync);
        reviews.MapPost("/{id:guid}/reject", RejectAsync);
        reviews.MapPost("/{id:guid}/restore", RestoreAsync);

        var disputes = app.MapModerationGroup("/admin/disputes", "Admin · Litiges");
        disputes.MapGet("/{id:guid}", GetDisputeAsync);
        disputes.MapGet("/by-order/{orderId:guid}", DisputesByOrderAsync);
        disputes.MapPost("/{id:guid}/review", StartReviewAsync);
        disputes.MapPost("/{id:guid}/resolve", ResolveAsync);
        disputes.MapPost("/{id:guid}/escalate", EscalateAsync);

        return app;
    }

    /// <summary>Avis d'un produit (vue modération).</summary>
    private static async Task<IResult> ReviewsByProductAsync(Guid productId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsByProductQuery(productId), ct)).Match(r => Results.Ok(r));

    /// <summary>Signale un avis.</summary>
    private static async Task<IResult> FlagAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new FlagReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette un avis (le retire de la note publique).</summary>
    private static async Task<IResult> RejectAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Restaure un avis signalé / rejeté.</summary>
    private static async Task<IResult> RestoreAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RestoreReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Détail d'un litige.</summary>
    private static async Task<IResult> GetDisputeAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDisputeQuery(id), ct)).Match(d => Results.Ok(d));

    /// <summary>Litiges rattachés à une commande.</summary>
    private static async Task<IResult> DisputesByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListDisputesByOrderQuery(orderId), ct)).Match(d => Results.Ok(d));

    /// <summary>Ouvre la revue d'un litige.</summary>
    private static async Task<IResult> StartReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new StartDisputeReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Résout un litige.</summary>
    private static async Task<IResult> ResolveAsync(Guid id, ResolveRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResolveDisputeCommand(id, request.Resolution), ct)).Match(() => Results.NoContent());

    /// <summary>Escalade un litige.</summary>
    private static async Task<IResult> EscalateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EscalateDisputeCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record ResolveRequest(string Resolution);
}
