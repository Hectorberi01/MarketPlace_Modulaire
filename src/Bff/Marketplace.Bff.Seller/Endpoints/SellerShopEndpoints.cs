using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RemoveKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SetPayoutAccount;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerProfile;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerMetadata;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — boutique : onboarding (devenir vendeur), profil, compte de
/// reversement et pièces KYB. Toutes les opérations sont scopées à la boutique
/// du jeton (résolue côté serveur).
/// </summary>
public static class SellerShopEndpoints
{
    public static IEndpointRouteBuilder MapSellerShopEndpoints(this IEndpointRouteBuilder app)
    {
        var shop = app.MapSellerGroup("/seller/shop", "Vendeur · Boutique");

        shop.MapGet("/", MyShopAsync);
        shop.MapPost("/", OnboardAsync);
        shop.MapPut("/profile", UpdateProfileAsync);
        // Informations société déclarées (raison sociale, RCCM, IFU…).
        shop.MapPut("/metadata", UpdateMetadataAsync);
        // Upload / mise à jour du logo de la boutique (multipart, champ « file »).
        shop.MapPost("/logo", UploadLogoAsync).DisableAntiforgery();
        shop.MapPut("/payout-account", SetPayoutAccountAsync);
        shop.MapPost("/kyb-documents", AddKybDocumentAsync);
        // Upload d'une pièce KYB (fichier) : envoyé au service média puis rattaché.
        shop.MapPost("/kyb-documents/upload", UploadKybDocumentAsync).DisableAntiforgery();
        // Téléchargement par le vendeur d'une de ses pièces (URL présignée temporaire).
        shop.MapGet("/kyb-documents/{id:guid}/download", DownloadKybDocumentAsync);
        // Suppression par le vendeur d'une de ses pièces KYB (scopée boutique, anti-IDOR).
        shop.MapDelete("/kyb-documents/{id:guid}", RemoveKybDocumentAsync);

        return app;
    }

    /// <summary>Ma boutique (profil, statut KYB, commission, note).</summary>
    private static async Task<IResult> MyShopAsync(ClaimsPrincipal user, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null ? SellerBff.NotASeller() : Results.Ok(seller);
    }

    /// <summary>Onboarding : crée la boutique rattachée au compte du jeton.</summary>
    private static async Task<IResult> OnboardAsync(ClaimsPrincipal user, OnboardRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterSellerCommand(userId, request.ShopName), ct)).Match(id => Results.Ok(new { sellerId = id }));

    /// <summary>Met à jour le profil de la boutique (nom, logo, description).</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ÉCRITURE : nom, logo et description sont la VITRINE. Une boutique suspendue
        // qui se renomme et change d'enseigne se refait une identité au moment précis
        // où la plateforme veut la reconnaître. Voir SellerRights.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        return denied
            ?? (await sender.Send(new UpdateSellerProfileCommand(seller!.Id, request.ShopName, request.LogoUrl, request.Description), ct)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Téléverse (ou remplace) le logo de la boutique. Le fichier part vers le service
    /// média public (même canal que les images produit), qui renvoie une URL publique ;
    /// celle-ci est ensuite enregistrée sur le profil via UpdateProfileCommand. Le nom
    /// et la description de la boutique sont préservés (seul le logo change).
    /// </summary>
    private static async Task<IResult> UploadLogoAsync(
        ClaimsPrincipal user, IFormFile file,
        ISender sender, IMediaStorage mediaStorage, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ÉCRITURE : le logo est de la vitrine, au même titre que le nom ci-dessus —
        // et il téléverse en plus un fichier public au nom de la plateforme.
        var (seller, denied) = await SellerBff.ResolveSellingSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var check = await UploadValidation.CheckImageAsync(file, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        // Le type RÉEL, déduit des octets — jamais `file.ContentType`, qui n'est que
        // la déclaration non vérifiée du client. C'est ce type qui sera stocké sur R2,
        // renvoyé aux navigateurs, et remis aux processeurs d'image.
        var upload = await mediaStorage.UploadAsync(
            new MediaUpload(file.FileName, check.ContentType!, ms.ToArray()), ct);
        if (upload.IsFailure)
        {
            return Results.BadRequest(new { error = upload.Error.Message });
        }

        // On conserve nom + description existants, on ne remplace que le logo.
        return (await sender.Send(new UpdateSellerProfileCommand(seller!.Id, seller!.ShopName, upload.Value.Url, seller!.Description), ct))
            .Match(() => Results.Ok(new { logoUrl = upload.Value.Url }));
    }

    /// <summary>
    /// Met à jour les informations société (metadata) de la boutique du jeton.
    ///
    /// PAS DE CONTRÔLE DE STATUT ICI, ET C'EST DÉLIBÉRÉ. Raison sociale, RCCM, IFU
    /// relèvent du DOSSIER de conformité, pas de l'offre commerciale. C'est souvent en
    /// les corrigeant qu'un vendeur suspendu lève sa suspension : lui fermer cette
    /// route en ferait un cul-de-sac dont seul le support pourrait le sortir.
    /// </summary>
    private static async Task<IResult> UpdateMetadataAsync(ClaimsPrincipal user, CompanyInfoRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new UpdateSellerMetadataCommand(seller.Id, request.ToDomain()), ct)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Définit le compte de reversement (MoMo, Wave, banque…).
    ///
    /// DROIT DE RETRAIT, PAS DROIT DE VENDRE. Ce n'est pas de la vitrine : c'est la
    /// DESTINATION des fonds. Une boutique fermée doit pouvoir corriger son numéro
    /// MoMo pour être payée de ce qu'elle a gagné — la bloquer confisquerait. Une
    /// boutique SUSPENDUE, elle, est sous mesure conservatoire : laisser réécrire la
    /// destination des fonds pendant qu'on instruit une fraude viderait la retenue de
    /// son sens, puisqu'il suffirait d'attendre la levée pour encaisser ailleurs.
    /// C'est exactement la frontière que trace SellerRights.CanWithdraw.
    /// </summary>
    private static async Task<IResult> SetPayoutAccountAsync(ClaimsPrincipal user, PayoutAccountRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var (seller, denied) = await SellerBff.ResolvePayableSellerAsync(user, sellers, ct);
        return denied
            ?? (await sender.Send(new SetPayoutAccountCommand(seller!.Id, request.Provider, request.AccountNumber, request.AccountName), ct)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Ajoute une pièce KYB (repasse la vérification en revue).
    ///
    /// AUCUN CONTRÔLE DE STATUT, comme sur les infos société : déposer une pièce est
    /// le geste même par lequel un vendeur suspendu ou en attente se met en règle. Le
    /// verrouiller punirait la mise en conformité.
    /// </summary>
    private static async Task<IResult> AddKybDocumentAsync(ClaimsPrincipal user, KybDocumentRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new AddKybDocumentCommand(seller.Id, request.Type, request.FileUrl), ct)).Match(id => Results.Ok(new { documentId = id }));
    }

    /// <summary>Téléverse une pièce KYB (multipart, champ « file ») vers le service média puis la rattache.</summary>
    private static async Task<IResult> UploadKybDocumentAsync(
        ClaimsPrincipal user, IFormFile file, [FromForm] string type,
        ISender sender, IKybDocumentStorage kybStorage, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var check = await UploadValidation.CheckDocumentAsync(file, ct);
        if (check.Error is { } invalid)
        {
            return invalid;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        // Stockage sécurisé sur Cloudflare R2 (pièces KYB sensibles). Le type enregistré est
        // le type RÉEL, pas celui déclaré : ce document sera ouvert par un agent de
        // conformité.
        var upload = await kybStorage.UploadAsync(
            new KybFileUpload(file.FileName, check.ContentType!, ms.ToArray()), ct);
        if (upload.IsFailure)
        {
            return Results.BadRequest(new { error = upload.Error.Message });
        }

        return (await sender.Send(new AddKybDocumentCommand(seller.Id, type, upload.Value.Url), ct))
            .Match(id => Results.Ok(new { documentId = id, url = upload.Value.Url }));
    }

    /// <summary>
    /// Renvoie une URL de téléchargement temporaire (présignée) pour une pièce KYB
    /// du vendeur. La propriété est vérifiée : on ne présigne que si la pièce
    /// appartient bien à la boutique du jeton (anti-IDOR).
    /// </summary>
    private static async Task<IResult> DownloadKybDocumentAsync(
        Guid id, ClaimsPrincipal user, IKybDocumentStorage kybStorage, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var doc = seller.KybDocuments.FirstOrDefault(d => d.Id == id);
        if (doc is null)
        {
            return SellerBff.NotFoundOrForbidden("kyb_document");
        }

        var presigned = kybStorage.CreatePresignedGetUrl(doc.FileUrl);
        return presigned.IsFailure
            ? Results.BadRequest(new { error = presigned.Error.Message })
            : Results.Ok(new { url = presigned.Value });
    }

    /// <summary>
    /// Supprime une pièce KYB de la boutique du jeton. La propriété est vérifiée en
    /// amont (la pièce doit figurer dans les documents de la boutique résolue) puis
    /// la commande, elle-même scopée au SellerId, effectue le retrait (anti-IDOR).
    /// Le binaire reste sur R2 (le port de stockage KYB n'expose pas de suppression) :
    /// seule la référence en base est retirée.
    /// </summary>
    private static async Task<IResult> RemoveKybDocumentAsync(
        Guid id, ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PAS DE PRÉ-CONTRÔLE SUR LE RÉSUMÉ EN CACHE.
        //
        // Ce garde-fou lisait `seller.KybDocuments`, c'est-à-dire un résumé servi par
        // le cache. Il n'apportait AUCUNE sécurité — `RemoveKybDocumentCommand` est
        // déjà scopée au vendeur, et le domaine ne cherche la pièce que parmi les
        // enfants de CET agrégat : un identifiant appartenant à une autre boutique ne
        // peut pas être trouvé. Anti-IDOR par construction, pas par vérification.
        //
        // En revanche il NUISAIT, dans les deux sens : un résumé périmé laissait
        // passer une pièce déjà supprimée (le vendeur recevait alors le message du
        // domaine, « Pièce KYB introuvable pour cette boutique », sur une ligne que
        // l'écran affichait encore), et aurait tout aussi bien pu refuser une pièce
        // légitime pas encore visible dans le cache.
        //
        // On laisse donc le domaine trancher : il lit la base, lui.
        // ─────────────────────────────────────────────────────────────────────────
        return (await sender.Send(new RemoveKybDocumentCommand(seller.Id, id), ct)).Match(() => Results.NoContent());
    }

    public sealed record OnboardRequest(string ShopName);
    public sealed record ProfileRequest(string ShopName, string? LogoUrl, string? Description);

    /// <summary>Infos société déclarées (toutes optionnelles) → metadata jsonb.</summary>
    public sealed record CompanyInfoRequest(
        string? LegalName,
        string? Rccm,
        string? Ifu,
        string? Address,
        /// <summary>
        /// Commune de la boutique. Accepte le CODE (« abomey-calavi ») ou le libellé
        /// (« Abomey-Calavi ») : le domaine résout les deux et normalise en code. C'est du
        /// déclaratif de dossier, donc facultatif — contrairement à l'adresse de livraison
        /// et au lieu d'expédition, où la commune est obligatoire.
        /// </summary>
        string? Commune,
        string? Activity,
        string? ManagerName,
        string? Phone)
    {
        public SellerCompanyInfo ToDomain()
            => new(LegalName, Rccm, Ifu, Address,
                   Marketplace.BuildingBlocks.Domain.Geography.BeninGeography.ResolveCommuneCode(Commune),
                   Activity, ManagerName, Phone);
    }
    public sealed record PayoutAccountRequest(string Provider, string AccountNumber, string AccountName);
    public sealed record KybDocumentRequest(string Type, string FileUrl);
}
