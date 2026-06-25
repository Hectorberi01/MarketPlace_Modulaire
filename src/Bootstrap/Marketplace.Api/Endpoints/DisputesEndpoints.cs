using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Disputes.Application.Disputes;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Disputes : ouverture d'un litige par une partie,
/// fil de messages, et arbitrage par la plateforme (examen, décision, escalade).
/// </summary>
public static class DisputesEndpoints
{
    /// <summary>Enregistre les routes de litige.</summary>
    public static IEndpointRouteBuilder MapDisputesEndpoints(this IEndpointRouteBuilder app)
    {
        var disputes = app.MapGroup("/api/disputes").WithTags("Disputes").RequireAuthorization();

        disputes.MapPost("/", OpenAsync).WithName("OpenDispute");
        disputes.MapGet("/mine", ListMineAsync).WithName("ListMyDisputes");
        disputes.MapGet("/order/{orderId:guid}", ListByOrderAsync).WithName("ListDisputesByOrder");
        disputes.MapGet("/{id:guid}", GetAsync).WithName("GetDispute");
        disputes.MapPost("/{id:guid}/messages", AddMessageAsync).WithName("AddDisputeMessage");

        // Arbitrage : réservé Admin / Modérateur.
        disputes.MapPost("/{id:guid}/review", ReviewAsync).WithName("StartDisputeReview").RequireAuthorization(p => p.RequireRole("Admin", "Moderator"));
        disputes.MapPost("/{id:guid}/resolve", ResolveAsync).WithName("ResolveDispute").RequireAuthorization(p => p.RequireRole("Admin", "Moderator"));
        disputes.MapPost("/{id:guid}/escalate", EscalateAsync).WithName("EscalateDispute").RequireAuthorization(p => p.RequireRole("Admin", "Moderator"));

        return app;
    }

    /// <summary>Ouvre un litige sur une commande.</summary>
    private static async Task<IResult> OpenAsync(OpenBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new OpenDisputeCommand(userId, body.OrderId, body.Type, body.Message, body.PhotoUrl), ct);
        return result.Match(id => Results.Created($"/api/disputes/{id}", new { id }));
    }

    /// <summary>Liste les litiges de l'utilisateur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyDisputesQuery(userId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Liste les litiges d'une commande.</summary>
    private static async Task<IResult> ListByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListDisputesByOrderQuery(orderId), ct)).Match(items => Results.Ok(items));

    /// <summary>Récupère un litige et son fil de messages.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDisputeQuery(id), ct)).Match(item => Results.Ok(item));

    /// <summary>Ajoute un message (et preuve) au litige.</summary>
    private static async Task<IResult> AddMessageAsync(Guid id, MessageBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddDisputeMessageCommand(id, userId, body.Body, body.PhotoUrl), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Passe le litige en examen.</summary>
    private static async Task<IResult> ReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new StartDisputeReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Tranche le litige.</summary>
    private static async Task<IResult> ResolveAsync(Guid id, ResolveBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResolveDisputeCommand(id, body.Resolution), ct)).Match(() => Results.NoContent());

    /// <summary>Escalade le litige.</summary>
    private static async Task<IResult> EscalateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EscalateDisputeCommand(id), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record OpenBody(Guid OrderId, string Type, string Message, string? PhotoUrl);
    public sealed record MessageBody(string Body, string? PhotoUrl);
    public sealed record ResolveBody(string Resolution);
}
