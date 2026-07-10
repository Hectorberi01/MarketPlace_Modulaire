using MediatR;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ActivateSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;
using Marketplace.Modules.Notifications.Application.Notifications;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>BFF Admin — gouvernance des vendeurs : KYB, activation/suspension.</summary>
public static class AdminSellerEndpoints
{
    public static IEndpointRouteBuilder MapAdminSellerEndpoints(this IEndpointRouteBuilder app)
    {
        var sellers = app.MapAdminGroup("/admin/sellers", "Admin · Vendeurs");

        sellers.MapGet("/", ListAsync);
        // Création d'un compte vendeur par l'admin : crée le compte utilisateur puis la boutique.
        sellers.MapPost("/register", RegisterSellerAccountAsync);
        sellers.MapGet("/{id:guid}", GetAsync);
        sellers.MapPost("/{id:guid}/kyb/approve", ApproveKybAsync);
        sellers.MapPost("/{id:guid}/kyb/reject", RejectKybAsync);
        // Lien de consultation temporaire (présigné) d'une pièce KYB stockée en privé sur R2.
        sellers.MapGet("/kyb/document-url", KybDocumentUrlAsync);
        // Téléversement par l'admin d'une pièce KYB au nom d'un vendeur (multipart : file + type).
        sellers.MapPost("/{id:guid}/kyb-documents/upload", UploadKybDocumentAsync).DisableAntiforgery();
        sellers.MapPost("/{id:guid}/activate", ActivateAsync);
        sellers.MapPost("/{id:guid}/suspend", SuspendAsync);
        // TEST push : envoie une notification au compte du vendeur (in-app + push FCM),
        // pour vérifier que le vendeur reçoit bien les notifications sur son app.
        sellers.MapPost("/{id:guid}/test-notification", SendTestNotificationAsync);

        return app;
    }

    /// <summary>
    /// TEST push : résout le compte propriétaire de la boutique et lui envoie une
    /// notification via le dispatcher (donc in-app + push FCM sur ses appareils).
    /// Permet de valider, depuis la console admin, que les vendeurs reçoivent les push.
    /// </summary>
    private static async Task<IResult> SendTestNotificationAsync(
        Guid id, ISender sender, NotificationDispatcher dispatcher, CancellationToken ct)
    {
        var seller = await sender.Send(new GetSellerQuery(id), ct);
        if (seller.IsFailure)
        {
            return seller.Match(_ => Results.NoContent()); // mappe l'échec (404) proprement
        }

        await dispatcher.NotifyAsync(
            seller.Value.UserId,
            "🔔 Notification de test",
            "Test envoyé depuis la console admin. Si vous recevez ce push, les notifications vendeur fonctionnent.",
            "Test",
            null,
            ct);

        return Results.NoContent();
    }

    /// <summary>Liste des vendeurs (back-office).</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListSellersQuery(), ct)).Match(s => Results.Ok(s));

    /// <summary>
    /// Crée un compte vendeur (par l'admin) : d'abord le compte utilisateur (Identity),
    /// puis la boutique (Sellers). Non transactionnel : si la 2ᵉ étape échoue, le compte
    /// utilisateur reste créé (à durcir en saga si nécessaire).
    /// </summary>
    private static async Task<IResult> RegisterSellerAccountAsync(RegisterSellerRequest request, ISender sender, CancellationToken ct)
    {
        var userResult = await sender.Send(new RegisterUserCommand(
            request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty, request.Password), ct);
        if (userResult.IsFailure)
        {
            return userResult.Match(_ => Results.Ok()); // mappe l'échec (400/409) proprement
        }

        var commission = request.CommissionRate is { } c ? c : 0.10m;
        return (await sender.Send(new RegisterSellerCommand(userResult.Value, request.ShopName, commission), ct))
            .Match(sellerId => Results.Ok(new { sellerId, userId = userResult.Value }));
    }

    /// <summary>Détail d'un vendeur.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSellerQuery(id), ct)).Match(s => Results.Ok(s));

    /// <summary>Valide le KYB.</summary>
    private static async Task<IResult> ApproveKybAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveKybCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette le KYB.</summary>
    private static async Task<IResult> RejectKybAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectKybCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Renvoie une URL de lecture temporaire (présignée, ~5 min) pour consulter une
    /// pièce KYB privée sur Cloudflare R2, à partir de sa référence stockée.
    /// </summary>
    private static IResult KybDocumentUrlAsync(string fileRef, IKybDocumentStorage storage)
        => storage.CreatePresignedGetUrl(fileRef).Match(url => Results.Ok(new { url }));

    /// <summary>
    /// Téléverse une pièce KYB pour un vendeur, au nom de l'admin : stockage privé
    /// sécurisé (R2) puis rattachement au dossier KYB du vendeur. Utilisé quand
    /// l'admin crée le compte et enregistre les pièces envoyées par le vendeur.
    /// </summary>
    private static async Task<IResult> UploadKybDocumentAsync(
        Guid id, [FromForm] string type, IFormFile file,
        ISender sender, IKybDocumentStorage kybStorage, CancellationToken ct)
    {
        if (UploadValidation.ValidateDocument(file) is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var upload = await kybStorage.UploadAsync(new KybFileUpload(file.FileName, file.ContentType, ms.ToArray()), ct);
        if (upload.IsFailure)
        {
            return Results.BadRequest(new { error = upload.Error.Message });
        }

        return (await sender.Send(new AddKybDocumentCommand(id, type, upload.Value.Url), ct))
            .Match(docId => Results.Ok(new { documentId = docId, url = upload.Value.Url }));
    }

    /// <summary>Active le vendeur (KYB validé + compte de reversement requis).</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateSellerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Suspend le vendeur.</summary>
    private static async Task<IResult> SuspendAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendSellerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Corps de création d'un compte vendeur par l'admin.</summary>
    public sealed record RegisterSellerRequest(
        string FirstName, string LastName, string Email, string? PhoneNumber, string Password,
        string ShopName, decimal? CommissionRate = null);
}
