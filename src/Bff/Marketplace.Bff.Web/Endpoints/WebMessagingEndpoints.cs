using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Messaging.Application.Conversations;

namespace Marketplace.Bff.Web.Endpoints;

/// <summary>BFF Web — messagerie acheteur ↔ vendeur (/web/conversations/*).</summary>
public static class WebMessagingEndpoints
{
    public static IEndpointRouteBuilder MapWebMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapGroup("/web/conversations").WithTags("Web · Messagerie").RequireAuthorization();

        conv.MapGet("/", ListAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);

        return app;
    }

    /// <summary>Fils de discussion de l'utilisateur.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyConversationsQuery(userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Messages d'un fil.</summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetConversationQuery(id, userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Envoie un message dans le fil.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct)).Match(() => Results.NoContent());

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
}
