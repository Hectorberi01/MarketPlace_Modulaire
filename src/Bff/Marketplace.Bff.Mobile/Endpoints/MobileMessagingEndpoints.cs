using MediatR;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Api.RealTime;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Messaging.Application.Conversations;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — messagerie acheteur ↔ vendeur (/mobile/conversations/*).</summary>
public static class MobileMessagingEndpoints
{
    public static IEndpointRouteBuilder MapMobileMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapGroup("/mobile/conversations").WithTags("Mobile · Messagerie").RequireAuthorization();

        conv.MapGet("/", ListAsync);
        conv.MapPost("/seller/{sellerId:guid}", StartWithSellerAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);

        return app;
    }

    /// <summary>
    /// Démarre (ou ouvre) une conversation avec un vendeur, identifié par son id
    /// boutique. Le destinataire réel est le compte utilisateur du vendeur.
    /// </summary>
    private static async Task<IResult> StartWithSellerAsync(
        ClaimsPrincipal user, Guid sellerId, StartWithSellerRequest? request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var seller = await sellers.GetSellerAsync(sellerId, ct);
        if (seller is null)
        {
            return Results.NotFound(new { error = "seller_not_found" });
        }

        if (seller.UserId == userId)
        {
            return Results.BadRequest(new { error = "cannot_message_self", message = "Vous ne pouvez pas vous contacter vous-même." });
        }

        // Un seul fil par contact (façon WhatsApp) : si une conversation existe
        // déjà avec ce vendeur, on la réutilise au lieu d'en créer une nouvelle.
        var existing = await sender.Send(new ListMyConversationsQuery(userId), ct);
        if (existing.IsSuccess)
        {
            var match = existing.Value.FirstOrDefault(c => c.ParticipantIds.Contains(seller.UserId));
            if (match is not null)
            {
                return Results.Ok(new { conversationId = match.Id });
            }
        }

        var message = string.IsNullOrWhiteSpace(request?.Message)
            ? $"Bonjour, je vous contacte au sujet de votre boutique « {seller.ShopName} »."
            : request!.Message!;

        var result = await sender.Send(new StartConversationCommand(userId, seller.UserId, "Seller", sellerId, message), ct);
        return result.Match(id => Results.Ok(new { conversationId = id }));
    }

    /// <summary>
    /// Fils de discussion de l'utilisateur, projetés pour le client (titre = nom
    /// du correspondant résolu via Identity, dernier message, non-lus).
    /// </summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, IIdentityModuleApi identity, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
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
            var title = "Vendeur";
            if (otherId != Guid.Empty)
            {
                var u = await identity.GetUserAsync(otherId, ct);
                if (u is not null)
                {
                    var name = $"{u.FirstName} {u.LastName}".Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        title = name;
                    }
                }
            }

            var last = c.Messages.LastOrDefault();
            conversations.Add(new
            {
                id = c.Id,
                title,
                lastMessage = last?.Body ?? string.Empty,
                updatedAtUtc = c.LastMessageAtUtc,
                unreadCount = c.Messages.Count(m => m.SenderId != userId && m.ReadAtUtc is null),
            });
        }

        return Results.Ok(conversations);
    }

    /// <summary>
    /// Messages d'un fil, sous forme de liste plate, avec un drapeau <c>fromMe</c>
    /// par message (l'expéditeur est-il l'utilisateur courant ?) — indispensable
    /// pour aligner les bulles côté client.
    /// </summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetConversationQuery(id, userId), ct);
        if (result.IsFailure)
        {
            return result.Match(c => Results.Ok(c));
        }

        // Ouvrir le fil = le marquer comme lu (remet le badge de non-lus à zéro).
        await sender.Send(new MarkConversationReadCommand(id, userId), ct);

        var messages = result.Value.Messages.Select(m => new
        {
            id = m.Id,
            senderId = m.SenderId,
            body = m.Body,
            attachments = m.Attachments,
            readAtUtc = m.ReadAtUtc,
            createdAtUtc = m.CreatedAtUtc,
            fromMe = m.SenderId == userId,
        });

        return Results.Ok(messages);
    }

    /// <summary>Envoie un message dans le fil, puis pousse l'événement temps réel au groupe.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct);
        if (result.IsFailure)
        {
            return result.Match(() => Results.NoContent());
        }

        // Notifie en temps réel les participants connectés à la conversation.
        await hub.Clients.Group(ChatHub.Group(id)).SendAsync("message", new { conversationId = id.ToString() }, ct);

        // Met à jour le badge « non lus » des autres participants, même s'ils
        // n'ont pas la conversation ouverte (groupe utilisateur).
        var convo = await sender.Send(new GetConversationQuery(id, userId), ct);
        if (convo.IsSuccess)
        {
            foreach (var participantId in convo.Value.ParticipantIds.Where(p => p != userId))
            {
                await hub.Clients.Group(ChatHub.UserGroup(participantId))
                    .SendAsync("inbox", new { conversationId = id.ToString() }, ct);
            }
        }

        return Results.NoContent();
    }

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
    public sealed record StartWithSellerRequest(string? Message);
}
