namespace Marketplace.BuildingBlocks.Infrastructure.Files;

/// <summary>
/// Reconnaît le type RÉEL d'un fichier à ses premiers octets (« magic bytes »), au lieu de
/// croire ce que le client en dit.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE `Content-Type` D'UN UPLOAD EST UNE DÉCLARATION DU CLIENT. PAS UN FAIT.
///
/// `IFormFile.ContentType` vient de l'en-tête que le NAVIGATEUR (ou un `curl`, ou un
/// script) a bien voulu écrire. Rien ne l'atteste. `--form 'file=@shell.php;type=image/png'`
/// suffit à le fabriquer.
///
/// Ces quatre signatures sont, elles, dans le fichier lui-même. Un JPEG commence par
/// FF D8 FF, un PNG par 89 50 4E 47, et aucune quantité d'en-têtes forgés n'y changera rien.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>CE N'EST PAS UN ANTIVIRUS, ET IL NE FAUT PAS LE PRENDRE POUR TEL.</b> Un fichier
/// peut parfaitement commencer par FF D8 FF <i>et</i> contenir une charge malveillante plus
/// loin (polyglotte JPEG/PHP, JPEG portant un exploit de décodeur). Ce contrôle établit une
/// chose, et une seule : <b>le serveur décide de la nature du fichier, pas le client</b>.
/// C'est nécessaire, ce n'est pas suffisant. Un scan antivirus reste un chantier séparé.
/// </para>
/// </summary>
public static class FileSignature
{
    /// <summary>Nombre d'octets à lire pour reconnaître les formats supportés (WebP en exige 12).</summary>
    public const int HeaderBytes = 16;

    /// <summary>
    /// Type MIME réel du contenu, ou <c>null</c> si aucune signature connue ne correspond.
    ///
    /// <c>null</c> vaut REFUS : on n'accepte que ce qu'on sait reconnaître. L'inverse — tout
    /// accepter sauf ce qu'on sait mauvais — est une liste noire, et une liste noire est
    /// toujours incomplète.
    /// </summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        // JPEG : FF D8 FF — commun à toutes les variantes (JFIF, Exif, SPIFF…).
        if (StartsWith(header, stackalloc byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        // PNG : la signature complète sur 8 octets. Les quatre derniers (0D 0A 1A 0A) sont
        // là précisément pour détecter les transferts corrompus — les vérifier ne coûte rien.
        if (StartsWith(header, stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        // WebP : conteneur RIFF. « RIFF » aux octets 0-3, « WEBP » aux octets 8-11 — les
        // octets 4-7 portent la taille et varient. Vérifier « RIFF » seul ne suffirait pas :
        // WAV et AVI sont aussi des conteneurs RIFF.
        if (header.Length >= 12
            && StartsWith(header, "RIFF"u8)
            && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        // PDF : « %PDF- ».
        if (StartsWith(header, "%PDF-"u8))
        {
            return "application/pdf";
        }

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature)
        => header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
