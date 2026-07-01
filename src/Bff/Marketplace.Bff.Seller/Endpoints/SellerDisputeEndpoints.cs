using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Disputes.Application.Disputes;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — litiges. Le module ne lie pas les litiges à un vendeur ; on
/// utilise donc comme preuve d'implication le fait que le vendeur ait une
/// expédition sur la commande concernée (anti-fuite entre vendeurs).
/// </summary>
public static class SellerDisputeEndpoints
{
    public static IEndpointRouteBuilder MapSellerDisputeEndpoints(this IEndpointRouteBuilder app)
    {
        var disputes = app.MapGroup("/seller/disputes").WithTags("Vendeur · Litiges").RequireAuthorization();

        disputes.MapGet("/by-order/{orderId:guid}", ByOrderAsync);
        disputes.MapGet("/{id:guid}", GetAsync);
        disputes.MapPost("/{id:guid}/messages", AddMessageAsync);

        return app;
    }

    /// <summary>Litiges d'une commande où le vendeur est impliqué.</summary>
    private static async Task<IResult> ByOrderAsync(ClaimsPrincipal user, Guid orderId, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, orderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("order");
        }

        return (await sender.Send(new ListDisputesByOrderQuery(orderId), ct)).Match(d => Results.Ok(d));
    }

    /// <summary>Détail d'un litige (si le vendeur est impliqué dans sa commande).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var dispute = await sender.Send(new GetDisputeQuery(id), ct);
        if (dispute.IsFailure)
        {
            return dispute.Match(d => Results.Ok(d));
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, dispute.Value.OrderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("dispute");
        }

        return Results.Ok(dispute.Value);
    }

    /// <summary>Ajoute un message au litige (auteur = utilisateur du jeton).</summary>
    private static async Task<IResult> AddMessageAsync(ClaimsPrincipal user, Guid id, MessageRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var dispute = await sender.Send(new GetDisputeQuery(id), ct);
        if (dispute.IsFailure)
        {
            return dispute.Match(d => Results.Ok(d));
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, dispute.Value.OrderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("dispute");
        }

        return (await sender.Send(new AddDisputeMessageCommand(id, userId, request.Body, request.PhotoUrl), ct))
            .Match(() => Results.NoContent());
    }

    public sealed record MessageRequest(string Body, string? PhotoUrl);
}
