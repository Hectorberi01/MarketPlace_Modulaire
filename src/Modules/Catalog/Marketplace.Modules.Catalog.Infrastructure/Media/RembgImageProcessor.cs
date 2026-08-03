using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Détourage LOCAL via un service rembg (u2net) auto-hébergé.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// UN SEUL ALLER-RETOUR, CONTRE QUATRE POUR CLOUDINARY
///
/// L'adaptateur Cloudinary téléverse l'original, signe la requête, interroge le rendu
/// en boucle tant qu'il reçoit un 423, puis détruit l'asset distant. Ici : un POST,
/// une réponse. Le modèle tourne dans le conteneur voisin, il n'y a rien à stocker
/// ailleurs ni à nettoyer.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LE RÉENCODAGE EN JPEG N'EST PAS UN LUXE
///
/// rembg répond TOUJOURS en PNG — son serveur code « image/png » en dur. Or une photo
/// de vêtement en PNG pèse souvent trois à quatre fois son équivalent JPEG, et la
/// chaîne applique une limite de 5 Mo (`UploadValidation.MaxImageBytes`) : rendre le
/// PNG tel quel ferait échouer l'envoi APRÈS que le vendeur a validé son aperçu.
///
/// Deux autres raisons, moins visibles mais aussi coûteuses :
///  • Cloudinary rendait du JPEG. L'app mobile s'y fie et renomme le fichier en «.jpg»
///    après traitement : lui servir du PNG produirait des fichiers mal étiquetés.
///  • Un JPEG n'a pas de canal alpha. C'est NOUS qui aplatissons sur blanc, donc le
///    résultat ne dépend pas de ce que le service a bien voulu faire du fond.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class RembgImageProcessor : IImageProcessor, IImageProcessingAvailability
{
    public const string ClientName = "rembg-image-processor";

    /// <summary>
    /// Côté maximal du rendu, en pixels.
    ///
    /// ⚠️ CE PLAFOND EST UN GARDE-FOU MÉMOIRE, PAS UN RÉGLAGE ESTHÉTIQUE.
    ///
    /// `UploadValidation` ne borne que le POIDS (5 Mo) ; un JPEG de 5 Mo peut faire
    /// 8000 × 6000. Le décodage réclame alors largeur × hauteur × 4 octets — environ
    /// 190 Mo — et la surface de destination autant. Près d'un demi-gigaoctet pour UNE
    /// photo, dans des conteneurs plafonnés à 512 Mo.
    ///
    /// Et ces allocations sont NON MANAGÉES : le ramasse-miettes ne les voit pas, le
    /// processus ne lève pas d'OutOfMemoryException — il reçoit un SIGKILL du noyau.
    /// Une panne sans exception, sans trace, sans corrélation évidente.
    ///
    /// 2000 px est la valeur à laquelle l'app mobile réduit déjà ses prises de vue
    /// (`_maxSide`) : la chaîne reste cohérente, et le pic mémoire tombe à ~16 Mo.
    /// </summary>
    private const int MaxSide = 2000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RembgOptions _options;
    private readonly RembgHealth _health;
    private readonly ILogger<RembgImageProcessor> _logger;

    public RembgImageProcessor(
        IHttpClientFactory httpClientFactory,
        RembgOptions options,
        RembgHealth health,
        ILogger<RembgImageProcessor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _health = health;
        _logger = logger;
    }

    /// <summary>
    /// Reflète la santé OBSERVÉE, pas la simple présence d'une configuration : un
    /// conteneur arrêté ne doit pas faire promettre un détourage aux interfaces.
    /// </summary>
    public bool IsAvailable => _health.IsHealthy;

    public async Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ClientName);

            // On ne passe PAS `bgc` : le service rendrait un PNG opaque pleine couleur,
            // bien plus lourd à transférer et à décoder, pour un fond blanc que nous
            // reposons de toute façon nous-mêmes. Un PNG à canal alpha, majoritairement
            // transparent, se compresse beaucoup mieux.
            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/remove";

            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            // Le champ s'appelle « file » — c'est le nom du paramètre côté serveur.
            form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName);
            // `EffectiveModel`, jamais `Model` : la liste blanche de licences s'applique ici.
            form.Add(new StringContent(_options.EffectiveModel), "model");
            // Lissage du masque. Il érode légèrement les contours fins : c'est un
            // compromis, pas une amélioration gratuite. Sur des photos de vêtements
            // prises en intérieur, le gain sur les bords dentelés l'emporte.
            form.Add(new StringContent("true"), "ppm");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds)));

            using var response = await client.PostAsync(url, form, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("rembg : détourage échoué ({Code}) : {Body}", (int)response.StatusCode, Trim(body));
                _health.MarkFailure();
                return Error.Failure("image.process.render_failed",
                    $"Détourage indisponible ({(int)response.StatusCode}).");
            }

            var cutout = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (cutout.Length == 0)
            {
                _health.MarkFailure();
                return Error.Failure("image.process.empty", "Le service de détourage a renvoyé une image vide.");
            }

            // L'orientation se lit sur l'ORIGINAL : le PNG produit par rembg n'a plus
            // d'EXIF (voir `ReadOrientation`).
            var jpeg = ToOpaqueJpeg(cutout, ReadOrientation(content), _options.JpegQuality);
            if (jpeg is null)
            {
                _health.MarkFailure();
                return Error.Failure("image.process.encode_failed", "L'image détourée n'a pas pu être réencodée.");
            }

            _health.MarkSuccess();
            return new ProcessedImage(jpeg, "image/jpeg");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Annulation venue de l'APPELANT (client parti) : on la laisse remonter, et
            // on ne l'impute pas au service — il n'y est pour rien.
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("rembg : délai de traitement dépassé ({Seconds} s).", _options.TimeoutSeconds);
            _health.MarkFailure();
            return Error.Failure("image.process.timeout", "Le détourage a pris trop de temps.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "rembg : traitement d'image en erreur.");
            _health.MarkFailure();
            return Error.Failure("image.process.error", "Le service de détourage est injoignable.");
        }
    }

    /// <summary>
    /// Orientation EXIF de l'image d'ORIGINE.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Un téléphone tenu à la verticale enregistre souvent les pixels À PLAT et note
    /// la rotation dans l'EXIF ; les visionneuses l'appliquent à l'affichage. rembg
    /// travaille sur les pixels bruts et rend un PNG, format qui ne transporte pas
    /// cette information.
    ///
    /// Sans reprise explicite, le détourage ressortait donc COUCHÉ là où l'original
    /// s'affichait droit — sur toutes les photos prises en portrait, c'est-à-dire la
    /// quasi-totalité des photos de vendeurs.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static SKEncodedOrigin ReadOrientation(byte[] original)
    {
        try
        {
            using var data = SKData.CreateCopy(original);
            using var codec = SKCodec.Create(data);
            return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
        }
        catch
        {
            // Orientation illisible : on ne tourne rien. Une photo droite vaut mieux
            // qu'une photo tournée au hasard.
            return SKEncodedOrigin.TopLeft;
        }
    }

    /// <summary>
    /// Aplatit le PNG détouré sur du blanc OPAQUE, applique l'orientation, réduit si
    /// nécessaire, et réencode en JPEG.
    /// </summary>
    private static byte[]? ToOpaqueJpeg(byte[] png, SKEncodedOrigin origin, int quality)
    {
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DIMENSIONS LUES AVANT DÉCODAGE.
        //
        // `SKCodec.Info` n'ouvre que l'en-tête. C'est ce qui permet de décider d'une
        // réduction AVANT d'allouer les pixels — décoder puis redimensionner aurait
        // déjà consommé la mémoire qu'on cherche à ne pas prendre.
        // ─────────────────────────────────────────────────────────────────────────
        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var longest = Math.Max(source.Width, source.Height);
        var scale = longest > MaxSide ? (float)MaxSide / longest : 1f;
        var scaled = scale < 1f ? codec.GetScaledDimensions(scale) : new SKSizeI(source.Width, source.Height);

        // Alpha PREMUL au décodage : c'est ce qui permet à Skia de composer le sujet
        // sur le blanc. Un décodage opaque écraserait la transparence en noir.
        var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, decodeInfo);
        if (bitmap is null)
        {
            return null;
        }

        // Un quart de tour échange largeur et hauteur.
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var targetWidth = swap ? bitmap.Height : bitmap.Width;
        var targetHeight = swap ? bitmap.Width : bitmap.Height;

        var surfaceInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(surfaceInfo);
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(OrientationMatrix(origin, bitmap.Width, bitmap.Height));
        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return encoded?.ToArray();
    }

    /// <summary>
    /// Transformation remettant l'image d'aplomb selon son orientation EXIF.
    ///
    /// Écrite en coefficients explicites plutôt qu'en composition de rotations et de
    /// symétries : chaque cas se relit et se vérifie à la main — `x' = a·x + b·y + c`,
    /// `y' = d·x + e·y + f` — là où un empilement de `PostConcat` s'inverse à la
    /// moindre inattention, et produit alors une image retournée que rien ne signale.
    ///
    /// <paramref name="w"/> et <paramref name="h"/> sont les dimensions de l'image
    /// DÉCODÉE. Les quatre cas diagonaux échangent les axes : la surface de
    /// destination doit être créée en h × w (c'est ce que fait `swap` chez l'appelant).
    ///
    /// Les huit cas de la norme sont traités, miroirs compris : ils coûtent une ligne
    /// chacun, et n'en couvrir que la moitié laisserait passer des photos inversées.
    /// </summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        //                                          a   b   c    d   e   f
        SKEncodedOrigin.TopLeft => Affine(1, 0, 0, 0, 1, 0),
        SKEncodedOrigin.TopRight => Affine(-1, 0, w, 0, 1, 0),   // miroir horizontal
        SKEncodedOrigin.BottomRight => Affine(-1, 0, w, 0, -1, h),   // 180°
        SKEncodedOrigin.BottomLeft => Affine(1, 0, 0, 0, -1, h),   // miroir vertical
        SKEncodedOrigin.LeftTop => Affine(0, 1, 0, 1, 0, 0),   // transposition
        SKEncodedOrigin.RightTop => Affine(0, -1, h, 1, 0, 0),   // 90° horaire
        SKEncodedOrigin.RightBottom => Affine(0, -1, h, -1, 0, w),   // transposition + miroir
        SKEncodedOrigin.LeftBottom => Affine(0, 1, 0, -1, 0, w),   // 270° horaire
        _ => Affine(1, 0, 0, 0, 1, 0),
    };

    private static SKMatrix Affine(float a, float b, float c, float d, float e, float f)
        => new(a, b, c, d, e, f, 0, 0, 1);

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
