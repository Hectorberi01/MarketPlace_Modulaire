namespace Marketplace.Modules.Sellers.Infrastructure.Storage;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// STOCKAGE OBJET DES PIÈCES KYB — S3-COMPATIBLE, ET NON PLUS SPÉCIFIQUE À R2.
///
/// Ce type s'appelait `CloudflareR2Options` et calculait son endpoint depuis un
/// identifiant de compte Cloudflare. Il ne pouvait donc viser que R2, alors que
/// l'adaptateur qui l'utilise ne fait rien de propre à Cloudflare : il signe en
/// AWS SigV4, en PATH-STYLE (« /bucket/clé »), ce qui est le protocole commun de
/// R2, de MinIO, de Ceph et de S3 lui-même.
///
/// La seule chose qui l'empêchait de parler à un MinIO était le nom du serveur,
/// codé en dur dans une propriété calculée. C'est désormais `ServiceUrl`.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE STOCKAGE-LÀ PEUT LÉGITIMEMENT QUITTER LE NUAGE.
///
/// Les pièces KYB sont des documents d'identité et d'entreprise de vendeurs
/// béninois. Faible volume, jamais servies publiquement, lues par des URL
/// présignées de cinq minutes lors d'une vérification d'administration. Rien dans
/// ce profil n'exige un réseau de bordure mondial — et l'argument de résidence des
/// données, lui, est réel.
///
/// Les IMAGES PRODUIT sont l'exact inverse : servies à chaque affichage de fiche,
/// depuis l'Afrique de l'Ouest, où la latence et le coût de la donnée mobile
/// comptent. Elles restent sur R2, dont l'egress est gratuit et le cache réparti.
/// Ce sont deux besoins différents ; les traiter pareil aurait été le raccourci.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>
    /// ENDPOINT DU SERVICE, quand il n'est pas déduit d'un compte Cloudflare.
    /// Exemples : « http://10.20.0.2:9000 » (MinIO par le tunnel),
    /// « https://s3.eu-west-3.amazonaws.com ». Renseigné, il l'emporte sur
    /// <see cref="AccountId"/>.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Identifiant de compte Cloudflare. Ignoré si <see cref="ServiceUrl"/> est renseigné.</summary>
    public string AccountId { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Nom du bucket recevant les pièces KYB.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Base URL publique pour lire les objets, si le bucket en expose une. Sur un
    /// bucket KYB elle doit rester VIDE : ces documents ne sont lus que par URL
    /// présignée. La renseigner ici rendrait des pièces d'identité accessibles à qui
    /// connaît le nom du fichier.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Région S3. R2 attend « auto ». MinIO accepte n'importe quoi mais doit
    /// recevoir la MÊME valeur que celle utilisée pour signer — d'où le fait qu'elle
    /// soit configurable plutôt que devinée.
    /// </summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// Endpoint effectif : celui qu'on a fourni, sinon celui de R2 déduit du compte.
    /// La barre oblique finale est retirée — l'adaptateur concatène « /bucket/clé »,
    /// et une double barre invaliderait la signature, qui porte sur l'URI canonique.
    /// </summary>
    public string Endpoint =>
        string.IsNullOrWhiteSpace(ServiceUrl)
            ? $"https://{AccountId}.r2.cloudflarestorage.com"
            : ServiceUrl.TrimEnd('/');

    /// <summary>
    /// L'adaptateur réel n'est actif que si de quoi joindre ET signer est renseigné.
    /// Un endpoint OU un compte suffit pour la première moitié ; les identifiants et
    /// le bucket restent obligatoires. Sans cela, c'est le stub hors-ligne qui sert.
    /// </summary>
    public bool IsConfigured =>
        (!string.IsNullOrWhiteSpace(ServiceUrl) || !string.IsNullOrWhiteSpace(AccountId))
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName);
}
