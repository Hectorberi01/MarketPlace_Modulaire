using MediatR;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Api.RealTime;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Messaging.Application.Conversations;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>BFF Vendeur — messagerie vendeur ↔ acheteur (/seller/conversations/*).</summary>
public static class SellerMessagingEndpoints
{
    public static IEndpointRouteBuilder MapSellerMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapSellerGroup("/seller/conversations", "Vendeur · Messagerie");

        conv.MapGet("/", ListAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);
        // Upload d'une image de pièce jointe (multipart, champ « file ») : renvoie l'URL
        // à joindre ensuite au message. Réutilise le stockage média de la plateforme.
        conv.MapPost("/attachments", UploadAttachmentAsync).DisableAntiforgery();

        // Réactions et suppression (voir sémantique dans les handlers).
        conv.MapPost("/{id:guid}/messages/{messageId:guid}/reactions", ReactAsync);
        conv.MapDelete("/{id:guid}/messages/{messageId:guid}", DeleteForEveryoneAsync);
        conv.MapDelete("/{id:guid}/messages/{messageId:guid}/for-me", HideForMeAsync);

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
            attachments = m.Attachments,
            sentAt = m.CreatedAtUtc,
            isDeleted = m.IsDeleted,
            reactions = m.Reactions.Select(r => new { emoji = r.Emoji, count = r.Count, mine = r.Mine }),
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
                    attachments = created.Attachments,
                    // DIAGNOSTIC TEMPORAIRE : nombre de pièces jointes REÇUES dans la
                    // requête (avant persistance). Comparé au count de `attachments`
                    // (après persistance+relecture), ça localise la panne.
                    receivedAttachments = request.Attachments?.Count ?? -1,
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

    /// <summary>
    /// Upload d'une image de pièce jointe de message. Valide le type RÉEL (déduit des
    /// octets), stocke sur le service média, renvoie l'URL à joindre au message.
    /// </summary>
    private static async Task<IResult> UploadAttachmentAsync(
        ClaimsPrincipal user, IFormFile file, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is null)
        {
            return Results.Unauthorized();
        }

        var check = await UploadValidation.CheckImageAsync(file, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var upload = await mediaStorage.UploadAsync(
            new MediaUpload(file.FileName, check.ContentType!, ms.ToArray()), ct);

        return upload.IsFailure
            ? Results.BadRequest(new { error = upload.Error.Message })
            : Results.Ok(new { url = upload.Value.Url });
    }

    /// <summary>
    /// Réagit à un message (emoji de la palette autorisée). Une seule réaction par
    /// personne : renvoyer le même emoji la retire, un autre la remplace. L'emoji est
    /// validé côté domaine — on n'accepte pas une chaîne libre.
    /// </summary>
    private static async Task<IResult> ReactAsync(
        ClaimsPrincipal user, Guid id, Guid messageId, ReactRequest request,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ReactToMessageCommand(id, messageId, userId, request.Emoji), ct);
        if (result.IsFailure)
        {
            return result.Match(() => Results.NoContent());
        }

        await NotifyAsync(hub, id, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Supprime un message POUR TOUT LE MONDE. Le domaine n'autorise que l'auteur, et
    /// conserve le corps en base (preuve en cas de litige) : seule la projection le
    /// remplace par « Message supprimé ».
    /// </summary>
    private static async Task<IResult> DeleteForEveryoneAsync(
        ClaimsPrincipal user, Guid id, Guid messageId,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new DeleteMessageForEveryoneCommand(id, messageId, userId), ct);
        if (result.IsFailure)
        {
            return result.Match(() => Results.NoContent());
        }

        await NotifyAsync(hub, id, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Masque un message POUR SOI seulement. L'autre participant continue de le voir :
    /// on ne notifie donc que… tout le monde quand même, car le fil du demandeur doit
    /// se rafraîchir (le message disparaît de SA vue via la projection).
    /// </summary>
    private static async Task<IResult> HideForMeAsync(
        ClaimsPrincipal user, Guid id, Guid messageId,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new HideMessageForMeCommand(id, messageId, userId), ct);
        if (result.IsFailure)
        {
            return result.Match(() => Results.NoContent());
        }

        await NotifyAsync(hub, id, ct);
        return Results.NoContent();
    }

    /// <summary>Signale au fil que son contenu a changé (les clients rechargent).</summary>
    private static Task NotifyAsync(IHubContext<ChatHub> hub, Guid conversationId, CancellationToken ct)
        => hub.Clients.Group(ChatHub.Group(conversationId))
            .SendAsync("message", new { conversationId = conversationId.ToString() }, ct);

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
    public sealed record ReactRequest(string Emoji);
}
