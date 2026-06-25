using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Messaging.Application.Conversations;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — messagerie acheteur ↔ vendeur (/mobile/conversations/*).</summary>
public static class MobileMessagingEndpoints
{
    public static IEndpointRouteBuilder MapMobileMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapGroup("/mobile/conversations").WithTags("Mobile · Messagerie").RequireAuthorization();

        conv.MapGet("/", ListAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);

        return app;
    }

    /// <summary>Fils de discussion de l'utilisateur.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyConversationsQuery(userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Messages d'un fil.</summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetConversationQuery(id, userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Envoie un message dans le fil.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct)).Match(() => Results.NoContent());

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
}
