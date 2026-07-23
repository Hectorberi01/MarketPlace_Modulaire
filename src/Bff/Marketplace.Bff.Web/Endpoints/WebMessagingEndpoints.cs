using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;
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
        // Upload d'une image de pièce jointe (multipart, champ « file ») → URL à joindre.
        conv.MapPost("/attachments", UploadAttachmentAsync).DisableAntiforgery();

        return app;
    }

    /// <summary>Fils de discussion de l'utilisateur.</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyConversationsQuery(userId), ct)).Match(c => Results.Ok(c));

    /// <summary>Messages d'un fil.</summary>
    private static async Task<IResult> MessagesAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetConversationQuery(id, userId), ct);
        if (result.IsFailure)
        {
            return result.Match(c => Results.Ok(c));
        }

        // Ouvrir le fil = le marquer comme lu. INDISPENSABLE aux accusés de lecture :
        // c'est ce qui pose `ReadAtUtc` sur les messages du VENDEUR et fait passer ses
        // coches de ✓ (envoyé) à ✓✓ (lu). Sans cet appel, un acheteur web ne
        // « validerait » jamais les messages reçus.
        await sender.Send(new MarkConversationReadCommand(id, userId), ct);

        return result.Match(c => Results.Ok(c));
    }

    /// <summary>Envoie un message dans le fil.</summary>
    private static async Task<IResult> SendAsync(ClaimsPrincipal user, Guid id, SendRequest request, ISender sender, CancellationToken ct)
        => WebBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SendMessageCommand(id, userId, request.Body, request.Attachments), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Upload d'une image de pièce jointe de message (acheteur web). Valide le type
    /// RÉEL (magic bytes), stocke sur le service média, renvoie l'URL à joindre.
    /// </summary>
    private static async Task<IResult> UploadAttachmentAsync(
        ClaimsPrincipal user, IFormFile file, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (WebBff.CurrentUserId(user) is null)
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

    public sealed record SendRequest(string Body, IReadOnlyList<string>? Attachments);
}
