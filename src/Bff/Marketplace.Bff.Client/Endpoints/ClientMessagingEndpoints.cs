using MediatR;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Api.RealTime;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Messaging.Application.Conversations;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Bff.Client.Endpoints;

/// <summary>BFF Client — messagerie acheteur ↔ vendeur (/client/conversations/*).</summary>
public static class ClientMessagingEndpoints
{
    public static IEndpointRouteBuilder MapClientMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var conv = app.MapGroup("/client/conversations").WithTags("Client · Messagerie").RequireAuthorization();

        conv.MapGet("/", ListAsync);
        conv.MapPost("/seller/{sellerId:guid}", StartWithSellerAsync);
        conv.MapGet("/{id:guid}/messages", MessagesAsync);
        conv.MapPost("/{id:guid}/messages", SendAsync);
        // Upload d'une image de pièce jointe (multipart, champ « file ») : renvoie l'URL
        // à joindre ensuite au message. Même stockage média que côté vendeur.
        conv.MapPost("/attachments", UploadAttachmentAsync).DisableAntiforgery();

        // Réactions et suppression — mêmes règles que côté vendeur (le domaine tranche).
        conv.MapPost("/{id:guid}/messages/{messageId:guid}/reactions", ReactAsync);
        conv.MapDelete("/{id:guid}/messages/{messageId:guid}", DeleteForEveryoneAsync);
        conv.MapDelete("/{id:guid}/messages/{messageId:guid}/for-me", HideForMeAsync);

        return app;
    }

    /// <summary>
    /// Démarre (ou ouvre) une conversation avec un vendeur, identifié par son id
    /// boutique. Le destinataire réel est le compte utilisateur du vendeur.
    /// </summary>
    private static async Task<IResult> StartWithSellerAsync(
        ClaimsPrincipal user, Guid sellerId, StartWithSellerRequest? request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
            isDeleted = m.IsDeleted,
            reactions = m.Reactions.Select(r => new { emoji = r.Emoji, count = r.Count, mine = r.Mine }),
        });

        return Results.Ok(messages);
    }

    /// <summary>Envoie un message dans le fil, puis pousse l'événement temps réel au groupe.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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

    /// <summary>
    /// Upload d'une image de pièce jointe de message (acheteur). Valide le type RÉEL
    /// (déduit des octets), stocke sur le service média, renvoie l'URL à joindre au
    /// message. Même implémentation que côté vendeur.
    /// </summary>
    private static async Task<IResult> UploadAttachmentAsync(
        ClaimsPrincipal user, IFormFile file, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is null)
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
    /// Réagit à un message (emoji de la palette autorisée). Bascule : le même emoji
    /// retire la réaction, un autre la remplace. L'emoji est validé par le domaine —
    /// on n'accepte jamais une chaîne libre venue du client.
    /// </summary>
    private static async Task<IResult> ReactAsync(
        ClaimsPrincipal user, Guid id, Guid messageId, ReactRequest request,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
    /// Supprime un message POUR TOUT LE MONDE. Le domaine n'autorise que l'auteur et
    /// conserve le corps en base (preuve en cas de litige) : c'est la projection qui
    /// renvoie « Message supprimé ».
    /// </summary>
    private static async Task<IResult> DeleteForEveryoneAsync(
        ClaimsPrincipal user, Guid id, Guid messageId,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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

    /// <summary>Masque un message pour SOI seulement (l'autre participant le voit toujours).</summary>
    private static async Task<IResult> HideForMeAsync(
        ClaimsPrincipal user, Guid id, Guid messageId,
        ISender sender, IHubContext<ChatHub> hub, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } userId)
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
    public sealed record StartWithSellerRequest(string? Message);
    public sealed record ReactRequest(string Emoji);
}
