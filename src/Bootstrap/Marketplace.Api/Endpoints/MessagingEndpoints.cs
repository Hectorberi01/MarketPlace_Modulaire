using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Messaging.Application.Conversations;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Messaging : conversations acheteur ↔ vendeur,
/// envoi de messages et accusés de lecture, pour l'utilisateur connecté.
/// </summary>
public static class MessagingEndpoints
{
    /// <summary>Enregistre les routes de messagerie.</summary>
    public static IEndpointRouteBuilder MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conversations = app.MapAuthenticatedGroup("/api/conversations").WithTags("Messaging");

        conversations.MapPost("/", StartAsync).WithName("StartConversation");
        conversations.MapGet("/", ListMineAsync).WithName("ListMyConversations");
        conversations.MapGet("/{id:guid}", GetAsync).WithName("GetConversation");
        conversations.MapPost("/{id:guid}/messages", SendAsync).WithName("SendMessage");
        conversations.MapPost("/{id:guid}/read", ReadAsync).WithName("MarkConversationRead");
        conversations.MapPost("/{id:guid}/archive", ArchiveAsync).WithName("ArchiveConversation");

        return app;
    }

    /// <summary>Démarre une conversation avec un destinataire.</summary>
    private static async Task<IResult> StartAsync(StartBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new StartConversationCommand(userId, body.RecipientId, body.ContextType, body.ContextId, body.Message), ct);
        return result.Match(id => Results.Created($"/api/conversations/{id}", new { id }));
    }

    /// <summary>Liste les conversations de l'utilisateur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyConversationsQuery(userId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Récupère une conversation (le demandeur doit en être participant).</summary>
    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetConversationQuery(id, userId), ct);
        return result.Match(item => Results.Ok(item));
    }

    /// <summary>Envoie un message dans la conversation.</summary>
    private static async Task<IResult> SendAsync(Guid id, SendBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SendMessageCommand(id, userId, body.Body, body.Attachments), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque les messages reçus comme lus.</summary>
    private static async Task<IResult> ReadAsync(Guid id, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new MarkConversationReadCommand(id, userId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Archive la conversation.</summary>
    private static async Task<IResult> ArchiveAsync(Guid id, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ArchiveConversationCommand(id, userId), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record StartBody(Guid RecipientId, string? ContextType, Guid? ContextId, string Message);
    public sealed record SendBody(string Body, IReadOnlyList<string>? Attachments);
}
