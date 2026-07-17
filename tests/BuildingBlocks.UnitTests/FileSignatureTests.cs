using FluentAssertions;
using Marketplace.BuildingBlocks.Infrastructure.Files;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LE TYPE D'UN FICHIER UPLOADÉ ÉTAIT CELUI QUE LE CLIENT VOULAIT BIEN DÉCLARER.
///
/// `IFormFile.ContentType` vient de l'en-tête multipart, écrit par l'appelant. Rien ne
/// l'atteste : `curl --form 'file=@payload.bin;type=image/png'` suffit à le forger.
///
/// Et ce type déclaré ne servait pas qu'à valider — il était ENSUITE stocké sur R2 puis
/// RENVOYÉ AUX NAVIGATEURS à chaque téléchargement, et passé aux processeurs d'image, qui
/// choisissent leur décodeur d'après lui. Le client décidait donc comment ses propres
/// octets seraient interprétés, plus tard, par nos serveurs et par nos utilisateurs.
///
/// Ces tests gardent la seule chose qui ne se falsifie pas : les octets eux-mêmes.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class FileSignatureTests
{
    private static byte[] Header(params byte[] bytes)
    {
        var buffer = new byte[FileSignature.HeaderBytes];
        bytes.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public void Reconnait_un_JPEG()
        => FileSignature.Detect(Header(0xFF, 0xD8, 0xFF, 0xE0)).Should().Be("image/jpeg");

    [Fact]
    public void Reconnait_un_PNG()
        => FileSignature.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
            .Should().Be("image/png");

    [Fact]
    public void Reconnait_un_WebP()
    {
        // RIFF….WEBP : « RIFF » en 0-3, la taille en 4-7, « WEBP » en 8-11.
        var webp = Header(
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x1A, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P');

        FileSignature.Detect(webp).Should().Be("image/webp");
    }

    [Fact]
    public void Reconnait_un_PDF()
        => FileSignature.Detect(Header((byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-'))
            .Should().Be("application/pdf");

    [Fact]
    public void Un_conteneur_RIFF_qui_n_est_PAS_du_WebP_est_REFUSE()
    {
        // WAV et AVI sont aussi des conteneurs RIFF. Ne vérifier que « RIFF » — ce qui est
        // l'erreur naturelle — laisserait passer n'importe quel média RIFF pour une image.
        var wav = Header(
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x24, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E');

        FileSignature.Detect(wav).Should().BeNull(
            "les 4 octets « WEBP » en position 8 sont ce qui distingue un WebP d'un WAV");
    }

    [Fact]
    public void Un_executable_deguise_en_image_est_REFUSE()
    {
        // Le cas concret : `curl --form 'file=@shell.exe;type=image/png'`. L'en-tête HTTP
        // annonçait « image/png », l'ancien contrôle le croyait, et le fichier était stocké.
        var pe = Header((byte)'M', (byte)'Z', 0x90, 0x00);   // exécutable Windows (PE)
        var elf = Header(0x7F, (byte)'E', (byte)'L', (byte)'F'); // exécutable Linux
        var zip = Header((byte)'P', (byte)'K', 0x03, 0x04);   // archive (ou .docx, .jar…)

        FileSignature.Detect(pe).Should().BeNull();
        FileSignature.Detect(elf).Should().BeNull();
        FileSignature.Detect(zip).Should().BeNull();
    }

    [Fact]
    public void Un_SVG_est_REFUSE__c_est_du_XML_executable_par_le_navigateur()
    {
        // Un SVG peut contenir <script>. Servi depuis notre domaine média, il exécuterait
        // du JavaScript dans le contexte de nos utilisateurs. Il n'est ni dans l'allowlist,
        // ni reconnu comme signature — donc refusé deux fois.
        var svg = Header((byte)'<', (byte)'s', (byte)'v', (byte)'g');

        FileSignature.Detect(svg).Should().BeNull();
    }

    [Fact]
    public void Un_fichier_INCONNU_est_refuse__on_n_accepte_que_ce_qu_on_reconnait()
    {
        // Politique de LISTE BLANCHE. L'inverse — tout accepter sauf ce qu'on sait mauvais —
        // serait une liste noire, et une liste noire est toujours incomplète.
        FileSignature.Detect(Header(0x01, 0x02, 0x03, 0x04)).Should().BeNull();
    }

    [Fact]
    public void Un_fichier_TROP_COURT_ne_fait_pas_planter_la_detection()
    {
        // Un fichier de 2 octets ne doit pas provoquer d'IndexOutOfRange. Le lecteur ne
        // remplit que ce qu'il a lu ; la détection doit s'en accommoder.
        FileSignature.Detect(new byte[] { 0xFF, 0xD8 }).Should().BeNull("FF D8 seul est incomplet : un JPEG exige FF D8 FF");
        FileSignature.Detect(ReadOnlySpan<byte>.Empty).Should().BeNull();
    }

    [Fact]
    public void Un_WebP_TRONQUE_avant_le_marqueur_WEBP_est_refuse()
    {
        // 8 octets : « RIFF » + la taille, mais le marqueur « WEBP » manque. Sans le
        // contrôle de longueur, on lirait au-delà du tampon.
        var truncated = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x1A, 0x00, 0x00, 0x00 };

        FileSignature.Detect(truncated).Should().BeNull();
    }
}
