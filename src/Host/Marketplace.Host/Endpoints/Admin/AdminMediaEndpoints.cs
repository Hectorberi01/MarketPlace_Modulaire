using Marketplace.Host.Extensions;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// BFF Admin — le téléversement des images éditoriales.
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// LA CONSOLE DEMANDAIT DE COLLER UNE URL À LA MAIN.
///
/// Le formulaire des bannières attendait « https://res.cloudinary.com/…/banniere.jpg », et
/// celui des événements la même chose pour son visuel d'en-tête. Autrement dit : déposer
/// le fichier ailleurs, dans un autre outil, avec d'autres identifiants, puis revenir en
/// recopier l'adresse. Une image publiée par erreur sous une mauvaise URL ne se voit qu'en
/// production, et une image jamais déposée se voit encore plus tard.
///
/// TOUT LE RESTE EXISTAIT DÉJÀ : `MediaUsage.ContentBanner` désignait le bon bucket,
/// `UploadValidation.CheckImageAsync` lit le type RÉEL dans les octets, et `IMediaStorage`
/// parle au service média. Il ne manquait que la route.
/// ═════════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AdminMediaEndpoints
{
    public static IEndpointRouteBuilder MapAdminMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var media = app.MapAdminGroup("/api/admin/media", "Admin · Médias");

        // `DisableAntiforgery` : requête multipart, comme les pièces KYB et les pièces
        // jointes de litige. Le groupe exige déjà le rôle Admin porté par le JWT.
        media.MapPost("/images", UploadImageAsync)
            .WithName("AdminUploadImage")
            .DisableAntiforgery();

        media.MapDelete("/{externalId}", DeleteImageAsync).WithName("AdminDeleteImage");

        return app;
    }

    /// <summary>
    /// Téléverse une image éditoriale et rend son URL publique.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════════
    /// L'USAGE EST FIXÉ ICI, ET N'EST PAS UN PARAMÈTRE.
    ///
    /// `MediaUsage` décide du bucket, donc du domaine public qui sert le fichier, donc de
    /// qui peut le lire. Le laisser choisir par l'appelant, c'est accepter qu'un jour une
    /// pièce de litige atterrisse dans le bucket public des bannières parce qu'un
    /// formulaire aura envoyé la mauvaise chaîne. La route est celle des images
    /// ÉDITORIALES — bannières, visuels d'événement, illustrations d'annonce — et elle
    /// range là où ces images-là doivent aller.
    /// ═════════════════════════════════════════════════════════════════════════════════
    ///
    /// L'IDENTIFIANT DISTANT EST RENDU AVEC L'URL, et c'est ce qui permet à l'appelant de
    /// supprimer l'image qu'il remplace. Sans lui, chaque changement de bannière
    /// abandonnerait un fichier facturé que plus personne ne sait nommer.
    /// </summary>
    private static async Task<IResult> UploadImageAsync(
        IFormFile? file, IMediaStorage mediaStorage, CancellationToken ct)
    {
        // Le type vient des OCTETS, jamais de ce que le navigateur déclare : c'est ce
        // type-là qui sera stocké, puis renvoyé aux navigateurs des acheteurs.
        var controle = await UploadValidation.CheckImageAsync(file, ct);
        if (controle.Error is { } invalide)
        {
            return invalide;
        }

        using var flux = new MemoryStream();
        await file!.CopyToAsync(flux, ct);

        var envoi = await mediaStorage.UploadAsync(
            new MediaUpload(file.FileName, controle.ContentType!, flux.ToArray(), MediaUsage.ContentBanner),
            ct);

        return envoi.IsFailure
            ? Results.BadRequest(new { error = envoi.Error.Message })
            : Results.Ok(new
            {
                url = envoi.Value.Url,
                externalId = envoi.Value.ExternalId,
                contentType = envoi.Value.ContentType,
            });
    }

    /// <summary>
    /// Supprime une image téléversée, à partir de son identifiant distant.
    ///
    /// APPELÉE AU REMPLACEMENT, par la console, une fois la nouvelle image enregistrée —
    /// jamais avant. L'ordre est celui de la photo de profil, et pour la même raison :
    /// supprimer d'abord laisserait, en cas d'échec de l'enregistrement, une bannière
    /// pointant vers un fichier disparu. Dans l'autre sens, le pire est un orphelin, que
    /// personne ne voit.
    /// </summary>
    private static async Task<IResult> DeleteImageAsync(
        string externalId, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Results.BadRequest(new { error = "Identifiant de média manquant." });
        }

        var retrait = await mediaStorage.DeleteAsync(externalId, ct);
        return retrait.IsFailure
            ? Results.BadRequest(new { error = retrait.Error.Message })
            : Results.NoContent();
    }
}
