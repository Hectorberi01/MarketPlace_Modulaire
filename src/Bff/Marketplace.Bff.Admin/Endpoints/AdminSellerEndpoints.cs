using MediatR;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ActivateSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveSellerReactivation;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.DeleteSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSellerByUser;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;
using Marketplace.Modules.Catalog.Application.Products.Queries.ListProductsBySeller;
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
        // Boutique rattachée à un COMPTE (fiche utilisateur → sa boutique). Placé avant
        // aucune ambiguïté : « by-user » ne satisfait pas la contrainte :guid ci-dessus.
        sellers.MapGet("/by-user/{userId:guid}", GetByUserAsync);
        // Catalogue du vendeur (fiche vendeur → ses produits).
        sellers.MapGet("/{id:guid}/products", ListProductsAsync);
        sellers.MapPost("/{id:guid}/kyb/approve", ApproveKybAsync);
        sellers.MapPost("/{id:guid}/kyb/reject", RejectKybAsync);
        // Lien de consultation temporaire (présigné) d'une pièce KYB stockée en privé sur R2.
        sellers.MapGet("/kyb/document-url", KybDocumentUrlAsync);
        // Téléversement par l'admin d'une pièce KYB au nom d'un vendeur (multipart : file + type).
        sellers.MapPost("/{id:guid}/kyb-documents/upload", UploadKybDocumentAsync).DisableAntiforgery();
        sellers.MapPost("/{id:guid}/activate", ActivateAsync);
        sellers.MapPost("/{id:guid}/suspend", SuspendAsync);
        // Réactivation d'un compte fermé (approbation de la demande vendeur) + suppression définitive.
        sellers.MapPost("/{id:guid}/reactivate", ReactivateAsync);
        sellers.MapDelete("/{id:guid}", DeleteAsync);
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
    /// Ouvre une boutique. DEUX cas, et il fallait les distinguer :
    ///
    ///  • <c>UserId</c> fourni → le compte existe déjà (un acheteur qui se lance dans
    ///    la vente, un compte créé plus tôt depuis la page Utilisateurs). On ne
    ///    recrée rien : on lui attache une boutique. Redemander son prénom, son e-mail
    ///    et un mot de passe n'aurait aucun sens — et provoquait un 409 « e-mail déjà
    ///    pris », puisque le formulaire tentait de recréer le compte.
    ///
    ///  • <c>UserId</c> absent → on crée le compte, puis la boutique.
    ///
    /// `RegisterSellerCommand` disait pourtant, en toutes lettres : « Onboarde un
    /// vendeur rattaché à un compte Identity EXISTANT ». La capacité était là depuis
    /// le début ; seul l'endpoint refusait de l'exposer.
    ///
    /// Non transactionnel : si l'ouverture de boutique échoue, le compte utilisateur
    /// reste créé (à durcir en saga si nécessaire).
    /// </summary>
    private static async Task<IResult> RegisterSellerAccountAsync(RegisterSellerRequest request, ISender sender, CancellationToken ct)
    {
        Guid userId;

        if (request.UserId is { } existingUserId)
        {
            userId = existingUserId;
        }
        else
        {
            // `CreatedByAdmin: true` — le vendeur a déjà été examiné (candidature, RCCM,
            // pièce d'identité) AVANT d'arriver dans ce formulaire. Le faire ensuite
            // patienter dans une file d'approbation qu'on remplirait soi-même serait
            // absurde : il doit pouvoir se connecter dès que vous lui transmettez son
            // mot de passe provisoire.
            var userResult = await sender.Send(new RegisterUserCommand(
                request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty,
                request.Password, CreatedByAdmin: true), ct);
            if (userResult.IsFailure)
            {
                return userResult.Match(_ => Results.Ok()); // mappe l'échec (400/409) proprement
            }

            userId = userResult.Value;
        }

        // ── LE RÔLE SELLER. Il n'était attribué NULLE PART. ───────────────────────
        //
        // `RegisterUserCommand` pose « Buyer » par défaut, et ce formulaire n'ajoutait
        // rien. Un vendeur créé ici n'avait donc que le rôle Buyer.
        //
        // C'était déjà faux ; c'est devenu bloquant depuis que la surface /seller/*
        // exige `RequireRole("Seller")` : sans ce rôle, le vendeur ne peut pas se
        // connecter à l'application vendeur. Du tout.
        //
        // On le pose ici, avant d'ouvrir la boutique — un vendeur sans son rôle est un
        // vendeur qui ne peut pas vendre.
        var rolesResult = await sender.Send(new ListRolesQuery(), ct);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Match(_ => Results.Ok());
        }

        var sellerRole = rolesResult.Value.FirstOrDefault(
            r => r.Name.Equals("Seller", StringComparison.OrdinalIgnoreCase));
        if (sellerRole is null)
        {
            // Le rôle est semé au démarrage (IdentityDataSeeder). S'il manque, la base
            // n'est pas dans un état exploitable : mieux vaut le dire que d'ouvrir une
            // boutique dont le gérant ne pourra jamais se connecter.
            return Results.Problem(
                title: "identity.role.seller_missing",
                detail: "Le rôle « Seller » est introuvable. La base n'a pas été correctement initialisée.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Idempotent côté domaine : User.AssignRole ignore un rôle déjà présent.
        var roleResult = await sender.Send(new AssignRoleCommand(userId, sellerRole.Id), ct);
        if (roleResult.IsFailure)
        {
            return roleResult.Match(() => Results.Ok());
        }

        var commission = request.CommissionRate is { } c ? c : 0.10m;
        return (await sender.Send(new RegisterSellerCommand(userId, request.ShopName, commission), ct))
            .Match(sellerId => Results.Ok(new { sellerId, userId }));
    }

    /// <summary>Détail d'un vendeur.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSellerQuery(id), ct)).Match(s => Results.Ok(s));

    /// <summary>
    /// Boutique rattachée à un compte utilisateur, ou 204 s'il n'en a pas.
    ///
    /// On renvoie « No Content » plutôt qu'un 404 : pour l'appelant (la fiche
    /// utilisateur), un acheteur sans boutique est un cas NORMAL, pas une erreur —
    /// et cela évite de faire remonter une erreur dans la console pour rien.
    /// </summary>
    private static async Task<IResult> GetByUserAsync(Guid userId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetSellerByUserQuery(userId), ct);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.NoContent();
    }

    /// <summary>Catalogue complet d'un vendeur (fiches produit, toutes en un appel).</summary>
    private static async Task<IResult> ListProductsAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListProductsBySellerQuery(id), ct)).Match(p => Results.Ok(p));

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
        var check = await UploadValidation.CheckDocumentAsync(file, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        // Pièce KYB : le cas qui fait le plus mal. Un « PDF » qui n'en est pas un serait
        // stocké tel quel, puis TÉLÉCHARGÉ ET OUVERT par un agent de conformité. C'est le
        // type réel qui est enregistré, pas celui que le vendeur a déclaré.
        var upload = await kybStorage.UploadAsync(
            new KybFileUpload(file.FileName, check.ContentType!, ms.ToArray()), ct);
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

    /// <summary>Approuve la demande de réactivation d'un compte fermé : le vendeur redevient actif.</summary>
    private static async Task<IResult> ReactivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveSellerReactivationCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Suppression DÉFINITIVE du vendeur (et archivage de ses produits).</summary>
    private static async Task<IResult> DeleteAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteSellerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Corps de création d'un compte vendeur par l'admin.</summary>
    /// <param name="UserId">
    /// Compte EXISTANT à qui rattacher la boutique. Renseigné, les champs d'identité
    /// (prénom, nom, e-mail, téléphone, mot de passe) sont ignorés — il n'y a rien à
    /// créer. Nul, on crée le compte puis la boutique.
    /// </param>
    public sealed record RegisterSellerRequest(
        string FirstName, string LastName, string Email, string? PhoneNumber, string Password,
        string ShopName, decimal? CommissionRate = null, Guid? UserId = null);
}
