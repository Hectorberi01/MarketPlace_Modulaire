using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Destination effective d'un usage : le bucket où écrire, le domaine public qui le
/// sert, et la racine de clé sous laquelle ranger.
/// </summary>
/// <param name="BucketName">Nom du bucket R2.</param>
/// <param name="PublicBaseUrl">Domaine public (r2.dev ou domaine personnalisé) servant ce bucket.</param>
/// <param name="KeyRoot">Premier segment de clé, sous le préfixe global. Voir la table de <see cref="CloudflareR2MediaOptions"/>.</param>
/// <param name="EstDedie">Faux quand l'usage est retombé sur le bucket par défaut faute de configuration.</param>
public sealed record R2BucketBinding(string BucketName, string PublicBaseUrl, string KeyRoot, bool EstDedie);

/// <summary>
/// Configuration Cloudflare R2 (compatible S3) pour les MÉDIAS PUBLICS.
/// Contrairement aux pièces KYB (privées), ces médias sont PUBLICS
/// (lisibles par les acheteurs) : chaque bucket doit exposer un domaine public
/// (r2.dev ou domaine personnalisé).
/// Secrets hors du dépôt. Section de config : « Storage:CloudflareR2Media ».
/// Tant que les identifiants ne sont pas renseignés, le stub/HBA reste utilisé.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// UN BUCKET PAR USAGE, ET UN REPLI QUI NE CASSE RIEN.
///
/// `BucketName` et `PublicBaseUrl` restent la destination PAR DÉFAUT : c'est le
/// bucket historique des images produit, et celui où retombe tout usage qu'on n'a pas
/// encore configuré. `Buckets` porte les destinations dédiées, une par
/// <see cref="MediaUsage"/>.
///
/// Un usage n'est dévié QUE si son bucket ET son domaine public sont tous deux
/// renseignés. Une moitié de configuration — bucket créé mais domaine public pas
/// encore rattaché, ce qui est l'état d'un bucket R2 fraîchement créé — produirait
/// des URL qui ne répondent pas. Le repli dépose alors dans le bucket par défaut,
/// sous la même racine de clé : l'image s'affiche, et la bascule se fera le jour où
/// le domaine sera branché, sans rien changer au code.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CloudflareR2MediaOptions
{
    /// <summary>
    /// Racine de clé par usage. Le premier segment d'une clé IDENTIFIE son usage :
    /// c'est ce qui permet à la suppression de retrouver le bucket d'un objet à
    /// partir de son seul identifiant, sans changer le format stocké en base.
    ///
    /// « products » pour les fiches produit est HISTORIQUE et ne doit pas changer :
    /// des milliers de clés déjà en base commencent par ce segment.
    /// </summary>
    private static readonly IReadOnlyDictionary<MediaUsage, string> RacinesDeCle =
        new Dictionary<MediaUsage, string>
        {
            [MediaUsage.ProductImage] = "products",
            [MediaUsage.ShopLogo] = "boutiques-logo",
            [MediaUsage.ShopBanner] = "boutiques-couverture",
            [MediaUsage.CategoryImage] = "categories",
            [MediaUsage.ContentBanner] = "bannieres",
            [MediaUsage.Avatar] = "avatars",
            [MediaUsage.ChatAttachment] = "messagerie"
        };

    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Bucket par défaut (images produit, et repli de tout usage non configuré).</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Base URL publique de lecture du bucket par défaut.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Préfixe de clé (dossier) commun à TOUS les buckets. Vide = racine.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public string Region { get; set; } = "auto";

    /// <summary>Destinations dédiées ACTIVES, par usage. Absent = repli sur le bucket par défaut.</summary>
    public Dictionary<MediaUsage, R2BucketBinding> Buckets { get; } = new();

    /// <summary>
    /// Noms de buckets NOMMÉS dans la configuration, qu'ils soient actifs ou non.
    ///
    /// La distinction compte : un bucket dont on connaît le nom mais pas encore le
    /// domaine public doit être CRÉÉ (c'est le préalable à l'étape manuelle de
    /// rattachement du domaine) sans pour autant recevoir déjà du trafic. Nommer le
    /// bucket le fait exister ; lui donner un domaine y déplace les téléversements.
    /// </summary>
    private readonly List<string> _bucketsNommes = new();

    /// <summary>
    /// Crée au démarrage les buckets déclarés qui n'existent pas encore.
    ///
    /// CE QUE CELA FAIT ET NE FAIT PAS. Un bucket créé par l'API S3 est PRIVÉ : R2
    /// n'expose aucun domaine public tant qu'on ne lui en rattache pas un depuis le
    /// tableau de bord, et l'API S3 ne sait pas le faire. Cette option évite donc
    /// l'échec « NoSuchBucket » au téléversement ; elle ne rend rien lisible.
    /// C'est aussi pourquoi le repli ci-dessus existe.
    /// </summary>
    public bool AutoCreateBuckets { get; set; } = true;

    public string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(PublicBaseUrl);

    /// <summary>Racine de clé d'un usage. Un usage inconnu retombe sur celle des produits.</summary>
    public static string RacineDe(MediaUsage usage)
        => RacinesDeCle.TryGetValue(usage, out var racine) ? racine : RacinesDeCle[MediaUsage.ProductImage];

    /// <summary>
    /// Déclare le bucket d'un usage. Le nom seul suffit à le faire créer au
    /// démarrage ; l'aiguillage des téléversements n'a lieu que lorsque le domaine
    /// public est renseigné lui aussi.
    /// </summary>
    public void DeclarerBucket(MediaUsage usage, string? bucketName, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return;
        }

        var nom = bucketName.Trim();
        if (!_bucketsNommes.Contains(nom, StringComparer.Ordinal))
        {
            _bucketsNommes.Add(nom);
        }

        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return;
        }

        Buckets[usage] = new R2BucketBinding(
            nom,
            publicBaseUrl.Trim().TrimEnd('/'),
            RacineDe(usage),
            EstDedie: true);
    }

    /// <summary>Destination effective d'un usage, repli compris.</summary>
    public R2BucketBinding Resoudre(MediaUsage usage)
        => Buckets.TryGetValue(usage, out var dedie)
            ? dedie
            : new R2BucketBinding(
                BucketName.Trim(),
                PublicBaseUrl.Trim().TrimEnd('/'),
                RacineDe(usage),
                EstDedie: false);

    /// <summary>
    /// Retrouve la destination d'un objet à partir de sa CLÉ, pour la suppression.
    ///
    /// La clé porte son usage dans son premier segment, sous le préfixe global. Une
    /// clé dont la racine n'est reconnue d'aucun usage — objet déposé avant cette
    /// séparation, ou racine renommée depuis — est rattachée au bucket par défaut :
    /// c'est là qu'elle se trouvait, puisque tout y allait.
    ///
    /// ANGLE MORT ASSUMÉ. Un objet écrit dans le bucket par défaut par REPLI, puis
    /// dont l'usage reçoit plus tard un bucket dédié, sera cherché dans le nouveau
    /// bucket : R2 répond 404, la suppression est considérée faite (elle est
    /// idempotente) et l'objet reste orphelin dans l'ancien bucket. Cela ne concerne
    /// aujourd'hui que les images produit, dont la destination ne bouge pas. Le jour
    /// où un autre usage deviendra supprimable, il faudra recopier ses objets AVANT
    /// de lui donner son bucket.
    /// </summary>
    public R2BucketBinding ResoudreParCle(string key)
    {
        var reste = key.TrimStart('/');

        var prefixe = KeyPrefix.Trim().Trim('/');
        if (prefixe.Length > 0 && reste.StartsWith(prefixe + "/", StringComparison.Ordinal))
        {
            reste = reste[(prefixe.Length + 1)..];
        }

        var racine = reste.Split('/', 2)[0];
        foreach (var paire in RacinesDeCle)
        {
            if (string.Equals(racine, paire.Value, StringComparison.Ordinal))
            {
                return Resoudre(paire.Key);
            }
        }

        return Resoudre(MediaUsage.ProductImage);
    }

    /// <summary>Noms de buckets distincts à garantir au démarrage : le bucket par défaut et tous ceux qui sont nommés.</summary>
    public IReadOnlyList<string> BucketsUtilises()
    {
        var noms = new List<string>();
        void Ajouter(string? nom)
        {
            var propre = nom?.Trim();
            if (!string.IsNullOrWhiteSpace(propre)
                && !noms.Contains(propre, StringComparer.Ordinal))
            {
                noms.Add(propre);
            }
        }

        Ajouter(BucketName);
        foreach (var nom in _bucketsNommes)
        {
            Ajouter(nom);
        }

        return noms;
    }
}
