using Microsoft.AspNetCore.Http;
using Marketplace.BuildingBlocks.Infrastructure.Files;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Résultat d'un contrôle d'upload.
/// </summary>
/// <param name="Error">Réponse 400 à renvoyer telle quelle, ou <c>null</c> si le fichier est valide.</param>
/// <param name="ContentType">
/// Le type MIME <b>RÉEL</b>, déduit des octets du fichier — <b>PAS</b> celui déclaré par le client.
///
/// ⚠️ <b>C'est CELUI-CI qu'il faut transmettre au stockage.</b> Continuer à passer
/// <c>file.ContentType</c> après avoir validé les magic bytes reviendrait à vérifier une
/// identité puis à recopier le faux nom : le fichier serait servi depuis R2, et remis aux
/// processeurs d'image, avec le type que le client a choisi.
/// </param>
public readonly record struct UploadCheck(IResult? Error, string? ContentType);

/// <summary>
/// Validation partagée des fichiers uploadés (multipart) : présence, taille max, et
/// vérification du type réel par les <b>octets d'en-tête</b>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AVANT, LE TYPE DU FICHIER ÉTAIT CELUI QUE LE CLIENT VOULAIT BIEN DÉCLARER.
///
/// Le contrôle se résumait à ceci :
///
///     var contentType = file.ContentType?.Split(';')[0]…    // ← déclaré par le CLIENT
///     if (!allowedContentTypes.Contains(contentType)) …
///
/// `IFormFile.ContentType` provient de l'en-tête multipart. Il est écrit par l'appelant, et
/// rien ne l'atteste : `curl --form 'file=@payload.bin;type=image/png'` suffit à le forger.
///
/// Et ce type déclaré ne servait pas qu'à valider — il était ENSUITE :
///   • stocké sur Cloudflare R2, et RENVOYÉ AUX NAVIGATEURS à chaque téléchargement ;
///   • passé aux processeurs d'image (Cloudinary), qui décident du décodeur à employer.
///
/// Autrement dit, le client choisissait comment ses propres octets seraient interprétés,
/// plus tard, par nos serveurs et par les navigateurs de nos utilisateurs. Le cas qui fait
/// mal : un « PDF » de pièce KYB qui n'en est pas un, stocké tel quel, puis ouvert par un
/// agent de conformité.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// Désormais le serveur LIT les premiers octets, en déduit le type, exige qu'il figure dans
/// l'allowlist, et c'est CE type — pas celui déclaré — qui est transmis au stockage.
/// </para>
/// </summary>
public static class UploadValidation
{
    public const long MaxImageBytes = 5 * 1024 * 1024;     // 5 Mo
    public const long MaxDocumentBytes = 10 * 1024 * 1024; // 10 Mo

    public static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/webp" };
    public static readonly string[] DocumentTypes = { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    /// <summary>
    /// Valide un fichier uploadé et renvoie son type MIME RÉEL.
    ///
    /// L'ordre des contrôles n'est pas indifférent : la TAILLE est vérifiée avant de lire le
    /// moindre octet. Lire l'en-tête d'un fichier de 2 Go pour découvrir ensuite qu'il est
    /// trop gros, ce serait offrir le DoS qu'on prétend fermer.
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

        // Aucune signature connue → REFUS. On n'accepte que ce qu'on sait reconnaître : une
        // liste noire (« tout sauf les .exe ») serait toujours incomplète.
        //
        // Message VOLONTAIREMENT identique dans les deux cas de refus ci-dessous : dire
        // « votre PNG est en réalité un ZIP » renseignerait un attaquant sur la finesse de
        // notre détection, et l'aiderait à la contourner.
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
