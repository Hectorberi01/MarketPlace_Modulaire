using Microsoft.AspNetCore.Http;
using Marketplace.BuildingBlocks.Infrastructure.Files;

namespace Marketplace.Host.Extensions;

/// <summary>Résultat d'un contrôle d'upload.</summary>
/// <param name="Error">Réponse 400 à renvoyer telle quelle, ou <c>null</c> si le fichier est valide.</param>
/// <param name="ContentType">
/// Le type MIME <b>RÉEL</b>, déduit des octets du fichier — pas celui déclaré par le
/// client. C'est celui-ci qu'il faut transmettre au stockage : passer
/// <c>file.ContentType</c> après avoir validé les magic bytes reviendrait à vérifier une
/// identité puis à recopier le faux nom.
/// </param>
public readonly record struct UploadCheck(IResult? Error, string? ContentType);

/// <summary>
/// Validation partagée des fichiers uploadés (multipart) : présence, taille max, et type
/// réel déduit des <b>octets d'en-tête</b>.
///
/// <c>IFormFile.ContentType</c> vient de l'en-tête multipart : il est écrit par l'appelant
/// et rien ne l'atteste. Or ce type était ensuite stocké sur R2 et renvoyé aux navigateurs,
/// puis passé aux processeurs d'image qui en déduisent le décodeur — le client choisissait
/// donc comment ses propres octets seraient interprétés par nos serveurs. Le serveur lit
/// désormais les premiers octets, exige que le type déduit figure dans l'allowlist, et
/// c'est CE type qui circule ensuite.
/// </summary>
public static class UploadValidation
{
    public const long MaxImageBytes = 5 * 1024 * 1024;     // 5 Mo
    public const long MaxDocumentBytes = 10 * 1024 * 1024; // 10 Mo

    public static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/webp" };
    public static readonly string[] DocumentTypes = { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    /// <summary>
    /// Valide un fichier uploadé et renvoie son type MIME RÉEL. L'ordre compte : la TAILLE
    /// est vérifiée avant de lire le moindre octet — lire l'en-tête d'un fichier de 2 Go
    /// pour le refuser ensuite offrirait le DoS qu'on prétend fermer.
    /// </summary>
    public static async Task<UploadCheck> CheckAsync(
        IFormFile? file,
        long maxBytes,
        IReadOnlyCollection<string> allowedContentTypes,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return new UploadCheck(Results.BadRequest(new { error = "Fichier manquant ou vide." }), null);
        }

        if (file.Length > maxBytes)
        {
            return new UploadCheck(
                Results.BadRequest(new { error = $"Fichier trop volumineux (max {maxBytes / (1024 * 1024)} Mo)." }),
                null);
        }

        // Lecture des octets d'en-tête. On ne lit QUE l'en-tête : inutile de charger le
        // fichier entier en mémoire pour savoir ce qu'il est.
        var header = new byte[FileSignature.HeaderBytes];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        var actualType = FileSignature.Detect(header.AsSpan(0, read));

        // Aucune signature connue → REFUS : on n'accepte que ce qu'on sait reconnaître, une
        // liste noire serait toujours incomplète. Message VOLONTAIREMENT identique dans les
        // deux refus : dire « votre PNG est en réalité un ZIP » renseignerait un attaquant
        // sur la finesse de la détection.
        if (actualType is null || !allowedContentTypes.Contains(actualType))
        {
            return new UploadCheck(
                Results.BadRequest(new
                {
                    error = "Type de fichier non autorisé. "
                          + $"Formats acceptés : {string.Join(", ", allowedContentTypes)}.",
                }),
                null);
        }

        // On renvoie le type RÉEL. L'appelant doit transmettre CELUI-CI au stockage —
        // jamais `file.ContentType`, qui reste la déclaration non vérifiée du client.
        return new UploadCheck(null, actualType);
    }

    /// <summary>Valide une image (JPEG/PNG/WebP, ≤ 5 Mo) et renvoie son type réel.</summary>
    public static Task<UploadCheck> CheckImageAsync(IFormFile? file, CancellationToken cancellationToken = default)
        => CheckAsync(file, MaxImageBytes, ImageTypes, cancellationToken);

    /// <summary>
    /// Valide un document (image ou PDF, ≤ 10 Mo) et renvoie son type réel — pièces KYB,
    /// justificatifs, pièces jointes.
    /// </summary>
    public static Task<UploadCheck> CheckDocumentAsync(IFormFile? file, CancellationToken cancellationToken = default)
        => CheckAsync(file, MaxDocumentBytes, DocumentTypes, cancellationToken);
}
