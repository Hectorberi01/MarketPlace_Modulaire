using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Returns.Application.Returns;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Returns (RMA) : demande de retour par l'acheteur,
/// traitement par le vendeur (approbation, réception, remboursement).
/// </summary>
public static class ReturnsEndpoints
{
    /// <summary>Enregistre les routes de retour.</summary>
    public static IEndpointRouteBuilder MapReturnsEndpoints(this IEndpointRouteBuilder app)
    {
        var returns = app.MapGroup("/api/returns").WithTags("Returns").RequireAuthorization();

        returns.MapPost("/", RequestAsync).WithName("RequestReturn");
        returns.MapGet("/mine", ListMineAsync).WithName("ListMyReturns");
        returns.MapGet("/seller/{sellerId:guid}", ListSellerAsync).WithName("ListSellerReturns");
        returns.MapGet("/{id:guid}", GetAsync).WithName("GetReturn");
        returns.MapPost("/{id:guid}/approve", ApproveAsync).WithName("ApproveReturn");
        returns.MapPost("/{id:guid}/reject", RejectAsync).WithName("RejectReturn");
        returns.MapPost("/{id:guid}/tracking", TrackingAsync).WithName("AddReturnTracking");
        returns.MapPost("/{id:guid}/received", ReceivedAsync).WithName("MarkReturnReceived");
        returns.MapPost("/{id:guid}/refund", RefundAsync).WithName("RefundReturn");

        return app;
    }

    /// <summary>Ouvre une demande de retour pour une ligne de commande de l'acheteur connecté.</summary>
    private static async Task<IResult> RequestAsync(RequestBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RequestReturnCommand(buyerId, body.OrderId, body.OfferId, body.Reason), ct);
        return result.Match(id => Results.Created($"/api/returns/{id}", new { id }));
    }

    /// <summary>Liste les retours de l'acheteur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyReturnsQuery(buyerId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Liste les retours d'un vendeur.</summary>
    private static async Task<IResult> ListSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListSellerReturnsQuery(sellerId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Récupère une demande de retour.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetReturnQuery(id), ct);
        return result.Match(item => Results.Ok(item));
    }

    /// <summary>Approuve la demande de retour.</summary>
    private static async Task<IResult> ApproveAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveReturnCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette la demande de retour.</summary>
    private static async Task<IResult> RejectAsync(Guid id, ReasonBody? body, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectReturnCommand(id, body?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());

    /// <summary>Renseigne le colis retour.</summary>
    private static async Task<IResult> TrackingAsync(Guid id, TrackingBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddReturnTrackingCommand(id, body.Carrier, body.TrackingNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Marque le colis retour comme réceptionné.</summary>
    private static async Task<IResult> ReceivedAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkReturnReceivedCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Déclenche le remboursement du retour.</summary>
    private static async Task<IResult> RefundAsync(Guid id, RefundBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveReturnRefundCommand(id, body.Amount), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record RequestBody(Guid OrderId, Guid OfferId, string Reason);
    public sealed record ReasonBody(string? Reason);
    public sealed record TrackingBody(string Carrier, string TrackingNumber);
    public sealed record RefundBody(decimal Amount);
}
