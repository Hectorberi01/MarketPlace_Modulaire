using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Messaging.Application.Conversations;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>BFF Vendeur — messagerie vendeur ↔ acheteur (/seller/conversations/*).</summary>
public static class SellerMessagingEndpoints
{
    public static IEndpointRouteBuilder MapSellerMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapGroup("/seller/conversations").WithTags("Vendeur · Messagerie").RequireAuthorization();

        conv.MapGet("/", ListAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);

        return app;
    }

    /// <summary>Fils de discussion du vendeur (utilisateur du jeton).</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyConversationsQuery(userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Messages d'un fil.</summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetConversationQuery(id, userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Envoie un message dans le fil.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct)).Match(() => Results.NoContent());

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
}
