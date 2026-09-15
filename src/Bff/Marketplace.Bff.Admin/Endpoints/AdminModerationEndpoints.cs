using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Contracts;
using Marketplace.Modules.Settlement.Contracts;
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
        disputes.MapGet("/", ListDisputesAsync);
        disputes.MapGet("/{id:guid}", GetDisputeAsync);
        disputes.MapGet("/by-order/{orderId:guid}", DisputesByOrderAsync);
        disputes.MapGet("/refunds-outstanding", RefundsOutstandingAsync);
        disputes.MapPost("/{id:guid}/review", StartReviewAsync);
        disputes.MapPost("/{id:guid}/resolve", ResolveAsync);
        disputes.MapPost("/{id:guid}/escalate", EscalateAsync);
        disputes.MapPost("/{id:guid}/reopen", ReopenAsync);
        // Multipart : champ « body » + fichier « file » optionnel (pièce jointe).
        disputes.MapPost("/{id:guid}/messages", AddMessageAsync).DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// Les remboursements ACCORDÉS par un litige et pas encore versés.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA CONTREPARTIE DE « LE LITIGE ARBITRE, IL NE PAIE PAS ».
    ///
    /// Trancher un litige en faveur de l'acheteur ne déplace aucun argent : c'est un
    /// choix, pas un oubli. `DisputeResolvedIntegrationEvent` n'a aucun consommateur
    /// côté Settlement, et n'en aura pas — un versement automatique décidé par un
    /// modérateur imputerait un vendeur ou la plateforme sans qu'un humain l'ait voulu.
    ///
    /// Mais une décision sans exécution ET sans trace est ce qu'était le système avant :
    /// l'acheteur recevait un e-mail lui promettant un remboursement, et rien n'existait
    /// nulle part pour dire à quiconque qu'il y avait quelque chose à payer. Cette route
    /// est cette trace. Elle ne crée aucun état : elle RAPPROCHE, à la lecture, ce qui a
    /// été accordé et ce qui a réellement été versé par les deux circuits.
    ///
    /// Ne rien stocker est délibéré : un drapeau « déjà réglé » qu'il faudrait penser à
    /// cocher divergerait du grand livre dès le premier oubli. Les sommes, elles, ne
    /// mentent pas.
    ///
    /// LIMITE CONNUE — `RefundBuyer` NE PORTE PAS DE MONTANT. `Dispute.Resolve` le met
    /// à null pour toute décision autre qu'un remboursement partiel. On ne peut donc pas
    /// calculer un reste dû pour ces litiges-là : on affiche ce qui reste remboursable
    /// SUR LA COMMANDE, et l'administrateur tranche. Exiger un montant y compris pour un
    /// remboursement intégral rendrait ce rapprochement exact ; c'est une évolution du
    /// contrat de résolution, pas de cette route.
    ///
    /// COÛT : une lecture de commande et deux sommes par litige. Acceptable sur une file
    /// de travail qui doit rester courte ; à revoir le jour où elle ne l'est plus.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> RefundsOutstandingAsync(
        ISender sender,
        IOrderingModuleApi orders,
        IReturnsModuleApi returns,
        ISettlementModuleApi settlement,
        CancellationToken ct)
    {
        var all = await sender.Send(new ListAllDisputesQuery(), ct);
        if (all.IsFailure)
        {
            return all.Match(d => Results.Ok(d));
        }

        var awarding = all.Value
            .Where(d => string.Equals(d.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
            .Where(d => d.Resolution is "RefundBuyer" or "PartialRefund")
            .OrderBy(d => d.ResolvedAtUtc)
            .ToList();

        var rows = new List<object>();

        foreach (var dispute in awarding)
        {
            var order = await orders.GetOrderAsync(dispute.OrderId, ct);
            if (order is null)
            {
                continue;
            }

            var byReturns = await returns.SumCommittedRefundsForOrderAsync(dispute.OrderId, ct);
            var byDirect = await settlement.SumDirectRefundsForOrderAsync(dispute.OrderId, ct);
            var refunded = byReturns + byDirect;

            // Accordé avec un montant : le reste dû est la différence. Accordé sans
            // montant (« intégralement ») : on ne peut que montrer le plafond restant.
            var covered = dispute.RefundAmount is { } awarded
                ? refunded >= awarded
                : refunded >= order.GrandTotal;

            if (covered)
            {
                continue;
            }

            rows.Add(new
            {
                disputeId = dispute.Id,
                orderId = dispute.OrderId,
                buyerId = order.BuyerId,
                resolution = dispute.Resolution,
                resolvedAt = dispute.ResolvedAtUtc,
                currency = order.Currency,
                orderTotal = order.GrandTotal,
                awarded = dispute.RefundAmount,
                refundedByReturns = byReturns,
                refundedDirect = byDirect,
                refundedTotal = refunded,
                outstanding = dispute.RefundAmount is { } a ? a - refunded : (decimal?)null,
                remainingOnOrder = order.GrandTotal - refunded,
            });
        }

        return Results.Ok(rows);
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

    /// <summary>Tous les litiges de la plateforme.</summary>
    private static async Task<IResult> ListDisputesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllDisputesQuery(), ct)).Match(d => Results.Ok(d));

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
        => (await sender.Send(new ResolveDisputeCommand(id, request.Resolution, request.RefundAmount), ct)).Match(() => Results.NoContent());

    /// <summary>Escalade un litige.</summary>
    private static async Task<IResult> EscalateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EscalateDisputeCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rouvre un litige tranché / escaladé.</summary>
    private static async Task<IResult> ReopenAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReopenDisputeCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Ajoute un message de l'administration au fil du litige.</summary>
    private static async Task<IResult> AddMessageAsync(
        Guid id, [FromForm] string body, IFormFile? file,
        ClaimsPrincipal user, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (AdminBff.CurrentUserId(user) is not { } authorId)
        {
            return Results.Unauthorized();
        }

        // Pièce jointe optionnelle : téléversée vers le stockage média public,
        // son URL est enregistrée comme PhotoUrl du message.
        string? photoUrl = null;
        if (file is { Length: > 0 })
        {
            var check = await UploadValidation.CheckImageAsync(file, ct);
            if (check.Error is { } invalid)
            {
                return invalid;
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            // Le type RÉEL, déduit des octets — jamais `file.ContentType`, qui n'est que
            // la déclaration non vérifiée du client. C'est ce type qui sera stocké sur R2,
            // renvoyé aux navigateurs, et remis aux processeurs d'image.
            var upload = await mediaStorage.UploadAsync(
                new MediaUpload(file.FileName, check.ContentType!, ms.ToArray(), MediaUsage.ChatAttachment), ct);
            if (upload.IsFailure)
            {
                return Results.BadRequest(new { error = upload.Error.Message });
            }
            photoUrl = upload.Value.Url;
        }

        return (await sender.Send(new AddDisputeMessageCommand(id, authorId, body, photoUrl), ct)).Match(() => Results.NoContent());
    }

    public sealed record ResolveRequest(string Resolution, decimal? RefundAmount = null);
}
