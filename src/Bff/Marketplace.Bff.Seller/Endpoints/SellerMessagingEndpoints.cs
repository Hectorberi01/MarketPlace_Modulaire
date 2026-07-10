using MediatR;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Api.RealTime;
using Marketplace.Modules.Identity.Contracts;
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

    /// <summary>
    /// Fils de discussion du vendeur, projetés vers la forme attendue par le
    /// dashboard (client, dernier message, non-lus). Le « client » est l'autre
    /// participant, résolu via Identity.
    /// </summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, IIdentityModuleApi identity, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyConversationsQuery(userId), ct);
        if (result.IsFailure)
        {
            return result.Match(c => Results.Ok(c));
        }

        var conversations = new List<object>();
        foreach (var c in result.Value)
        {
            var otherId = c.ParticipantIds.FirstOrDefault(p => p != userId);
            var customer = "Client";
            if (otherId != Guid.Empty)
            {
                var u = await identity.GetUserAsync(otherId, ct);
                if (u is not null)
                {
                    customer = $"{u.FirstName} {u.LastName}".Trim();
                }
            }

            var last = c.Messages.LastOrDefault();
            conversations.Add(new
            {
                id = c.Id,
                customer = string.IsNullOrWhiteSpace(customer) ? "Client" : customer,
                subject = c.ContextType,
                lastMessage = last?.Body ?? string.Empty,
                lastAt = c.LastMessageAtUtc,
                unread = c.Messages.Count(m => m.SenderId != userId && m.ReadAtUtc is null),
            });
        }

        return Results.Ok(conversations);
    }

    /// <summary>Messages d'un fil, en liste plate (id, fromSeller, body, sentAt).</summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetConversationQuery(id, userId), ct);
        if (result.IsFailure)
        {
            return result.Match(c => Results.Ok(c));
        }

        // Ouvrir le fil = le marquer comme lu (remet les non-lus à zéro).
        await sender.Send(new MarkConversationReadCommand(id, userId), ct);

        var messages = result.Value.Messages.Select(m => new
        {
            id = m.Id,
            fromSeller = m.SenderId == userId,
            body = m.Body,
            sentAt = m.CreatedAtUtc,
        });

        return Results.Ok(messages);
    }

    /// <summary>
    /// Envoie un message dans le fil et renvoie le message créé (le dashboard
    /// attend un objet Message en réponse, pas un 204 vide).
    /// </summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct);
        if (result.IsFailure)
        {
            return result.Match(() => Results.Ok());
        }

        // Notifie en temps réel les participants connectés à la conversation.
        await hub.Clients.Group(ChatHub.Group(id)).SendAsync("message", new { conversationId = id.ToString() }, ct);

        // Recharge le fil pour (a) notifier les autres participants et (b) renvoyer
        // le VRAI message persisté (id/date réels), pas un objet factice.
        var convo = await sender.Send(new GetConversationQuery(id, userId), ct);
        if (convo.IsSuccess)
        {
            foreach (var participantId in convo.Value.ParticipantIds.Where(p => p != userId))
            {
                await hub.Clients.Group(ChatHub.UserGroup(participantId))
                    .SendAsync("inbox", new { conversationId = id.ToString() }, ct);
            }

            // Dernier message émis par le vendeur = celui qu'on vient de créer.
            var created = convo.Value.Messages.LastOrDefault(m => m.SenderId == userId);
            if (created is not null)
            {
                return Results.Ok(new
                {
                    id = created.Id,
                    fromSeller = true,
                    body = created.Body,
                    sentAt = created.CreatedAtUtc,
                });
            }
        }

        // Repli si la relecture échoue : au moins renvoyer le corps envoyé.
        return Results.Ok(new
        {
            id = Guid.NewGuid(),
            fromSeller = true,
            body = request.Body,
            sentAt = DateTime.UtcNow,
        });
    }

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
}
