using MediatR;
using Microsoft.AspNetCore.Http;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Users.Commands.SetAvatar;

namespace Marketplace.Host.Extensions;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// LA PHOTO DE PROFIL, ÉCRITE UNE FOIS POUR LES TROIS APPLICATIONS.
///
/// Acheteur, vendeur et livreur posent la même photo sur le même compte : le geste est
/// identique, seule l'adresse de la route change. Le recopier dans les trois BFF serait
/// s'engager à corriger trois fois le jour où la taille maximale change, où le fichier
/// précédent cesse d'être supprimé, ou où un type d'image s'ajoute.
///
/// Ce qui reste propre à chaque BFF est ce qui doit l'être : la manière de retrouver
/// l'utilisateur dans le jeton, et l'endroit où la route est publiée.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AvatarUpload
{
    /// <summary>
    /// Téléverse la photo, l'enregistre sur le compte, puis supprime la précédente.
    ///
    /// L'ORDRE EST DÉLIBÉRÉ, et il est décrit sur <see cref="SetUserAvatarCommand"/> :
    /// on n'efface l'ancien fichier qu'une fois le nouveau enregistré. Si la suppression
    /// échoue, il reste un fichier orphelin — ce que personne ne voit. Dans l'autre
    /// sens, un échec d'enregistrement laisserait le compte pointant vers un fichier
    /// disparu, c'est-à-dire une image cassée à la place d'un visage.
    /// </summary>
    public static async Task<IResult> SetAsync(
        Guid userId,
        IFormFile? file,
        ISender sender,
        IMediaStorage mediaStorage,
        CancellationToken ct)
    {
        // Le type est déduit des OCTETS, jamais de ce que le client déclare : c'est ce
        // type-là qui finira stocké sur R2 et renvoyé aux navigateurs.
        var check = await UploadValidation.CheckImageAsync(file, ct);
        if (check.Error is { } invalide)
        {
            return invalide;
        }

        using var flux = new MemoryStream();
        await file!.CopyToAsync(flux, ct);

        var envoi = await mediaStorage.UploadAsync(
            new MediaUpload(file.FileName, check.ContentType!, flux.ToArray(), MediaUsage.Avatar), ct);
        if (envoi.IsFailure)
        {
            return Results.BadRequest(new { error = envoi.Error.Message });
        }

        var pose = await sender.Send(
            new SetUserAvatarCommand(userId, envoi.Value.Url, envoi.Value.ExternalId), ct);
        if (pose.IsFailure)
        {
            // Le compte n'a pas pris la photo : le fichier qu'on vient d'envoyer ne sera
            // servi à personne. On le retire tout de suite plutôt que de l'abandonner.
            await mediaStorage.DeleteAsync(envoi.Value.ExternalId, ct);
            return Results.BadRequest(new { error = pose.Error.Message });
        }

        await SupprimerPrecedentAsync(pose.Value, mediaStorage, ct);
        return Results.Ok(new { avatarUrl = envoi.Value.Url });
    }

    /// <summary>Retire la photo du compte et supprime le fichier.</summary>
    public static async Task<IResult> RemoveAsync(
        Guid userId, ISender sender, IMediaStorage mediaStorage, CancellationToken ct)
    {
        var retrait = await sender.Send(new SetUserAvatarCommand(userId, null, null), ct);
        if (retrait.IsFailure)
        {
            return Results.BadRequest(new { error = retrait.Error.Message });
        }

        await SupprimerPrecedentAsync(retrait.Value, mediaStorage, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Supprime le fichier remplacé, s'il y en avait un.
    ///
    /// L'ÉCHEC EST IGNORÉ, ET C'EST UN CHOIX. La photo est déjà enregistrée ; faire
    /// échouer la requête parce qu'un vieux fichier n'a pas pu être retiré donnerait à
    /// l'utilisateur une erreur pour une opération qui a réussi, et le pousserait à
    /// recommencer — ce qui créerait un orphelin de plus.
    /// </summary>
    private static async Task SupprimerPrecedentAsync(
        string? externalId, IMediaStorage mediaStorage, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            await mediaStorage.DeleteAsync(externalId, ct);
        }
    }
}
