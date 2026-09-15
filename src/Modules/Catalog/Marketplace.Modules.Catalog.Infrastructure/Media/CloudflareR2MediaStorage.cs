using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Stockage RÉEL des médias publics sur Cloudflare R2 (API compatible S3), signé
/// en AWS Signature V4 (sans SDK). Upload (PutObject) et suppression (DeleteObject)
/// pour respecter la contrainte : retirer une image d'un produit ou supprimer un
/// produit efface aussi le(s) fichier(s) sur R2. Les URLs renvoyées sont publiques.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE BUCKET N'EST PLUS UNIQUE : IL EST DÉDUIT DE L'USAGE.
///
/// Chaque <see cref="MediaUsage"/> résout vers un bucket, un domaine public et une
/// racine de clé — la table vit dans <see cref="CloudflareR2MediaOptions"/>. Le
/// bucket entre donc dans l'URI CANONIQUE de la signature : signer contre le mauvais
/// bucket produit un « SignatureDoesNotMatch » et non un « NoSuchBucket », ce qui est
/// la piste la plus trompeuse qui soit. C'est pourquoi `BuildSignedRequest` reçoit le
/// bucket en paramètre au lieu de le lire sur les options.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CloudflareR2MediaStorage : IMediaStorage
{
    public const string ClientName = "cloudflare-r2-media";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareR2MediaOptions _options;

    public CloudflareR2MediaStorage(IHttpClientFactory httpClientFactory, CloudflareR2MediaOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<Result<StoredMedia>> UploadAsync(MediaUpload file, CancellationToken cancellationToken = default)
    {
        try
        {
            var destination = _options.Resoudre(file.Usage);
            var key = BuildObjectKey(destination.KeyRoot, file.FileName);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            using var request = BuildSignedRequest(HttpMethod.Put, destination.BucketName, key, file.Content, contentType);
            request.Content = new ByteArrayContent(file.Content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var response = await CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<StoredMedia>(
                    Error.Failure("media.upload_failed", $"Cloudflare R2 — échec upload ({(int)response.StatusCode}) : {body}"));
            }

            var url = $"{destination.PublicBaseUrl}/{EncodeKey(key)}";
            return new StoredMedia(url, key, contentType);
        }
        catch (Exception ex)
        {
            return Result.Failure<StoredMedia>(Error.Failure("media.upload_error", $"Cloudflare R2 : {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<StoredMedia>>> UploadManyAsync(
        IReadOnlyList<MediaUpload> files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return Result.Success<IReadOnlyList<StoredMedia>>(Array.Empty<StoredMedia>());
        }

        var stored = new List<StoredMedia>(files.Count);
        foreach (var file in files)
        {
            var result = await UploadAsync(file, cancellationToken);
            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyList<StoredMedia>>(result.Error);
            }

            stored.Add(result.Value);
        }

        return Result.Success<IReadOnlyList<StoredMedia>>(stored);
    }

    public async Task<Result> DeleteAsync(string externalId, CancellationToken cancellationToken = default)
    {
        // externalId vide (média non rattaché à R2) : rien à supprimer.
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Result.Success();
        }

        var key = NormalizeKey(externalId);
        if (key is null)
        {
            // Identifiant qui n'est pas une clé de ce stockage — URL, remontée de
            // chemin, chaîne vide. On ne devine pas ce qu'il désigne, et surtout on
            // ne le résout pas en objet : ce serait exactement le chemin qui
            // permettait de supprimer le fichier d'autrui.
            return Result.Failure(Error.Validation(
                "media.external_id_invalid",
                "Identifiant de média invalide : une clé de stockage est attendue."));
        }

        // La clé porte son usage dans son premier segment : c'est ce qui permet de
        // retrouver le bucket sans changer le format stocké en base, et sans faire
        // confiance à quoi que ce soit venu de l'appelant.
        var destination = _options.ResoudreParCle(key);

        try
        {
            using var request = BuildSignedRequest(
                HttpMethod.Delete, destination.BucketName, key, Array.Empty<byte>(), contentType: null);

            var response = await CreateClient().SendAsync(request, cancellationToken);
            // 204 = supprimé ; 404 = déjà absent → on considère l'objet supprimé (idempotent).
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result.Failure(Error.Failure("media.delete_failed", $"Cloudflare R2 — échec suppression ({(int)response.StatusCode}) : {body}"));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Failure("media.delete_error", $"Cloudflare R2 : {ex.Message}"));
        }
    }

    // ---- Signature AWS SigV4 (PUT/DELETE) ----

    /// <summary>
    /// Signe une requête objet contre <paramref name="bucket"/>. Le bucket est un
    /// paramètre et NON une lecture des options : il change d'un usage à l'autre, et
    /// il entre dans l'URI canonique de la signature.
    /// </summary>
    private HttpRequestMessage BuildSignedRequest(
        HttpMethod method, string bucket, string key, byte[] payload, string? contentType)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ");
        var dateStamp = now.ToString("yyyyMMdd");
        // `Authority` et non `Host` : le port fait partie de l'en-tête `host` que
        // .NET enverra dès qu'il n'est pas celui du schéma, et donc de ce que
        // SigV4 doit signer. Sans effet sur R2 (https sur 443), mais le jour où cet
        // adaptateur viserait un service sur un port non standard, la signature
        // serait rejetée sans que rien n'indique pourquoi.
        var host = new Uri(_options.Endpoint).Authority;
        var payloadHash = Hex(SHA256.HashData(payload));
        var canonicalUri = "/" + bucket + "/" + EncodeKey(key);

        // En-têtes canoniques triés : content-type (si présent), host, x-amz-content-sha256, x-amz-date.
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        if (contentType is not null)
        {
            headers["content-type"] = contentType;
        }

        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalHeaders = string.Concat(headers.Select(h => $"{h.Key}:{h.Value}\n"));
        var canonicalRequest = $"{method.Method}\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var scope = $"{dateStamp}/{_options.Region}/s3/aws4_request";
        var stringToSign =
            "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

        var signingKey = GetSignatureKey(_options.SecretAccessKey, dateStamp, _options.Region, "s3");
        var signature = Hex(HmacSha256(signingKey, stringToSign));
        var authorization =
            $"AWS4-HMAC-SHA256 Credential={_options.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";

        var requestUri = $"{_options.Endpoint}/{bucket}/{EncodeKey(key)}";
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        // content-type est un en-tête de contenu : à poser sur request.Content par l'appelant.
        // (déjà géré via MediaTypeHeaderValue dans UploadAsync)
        return request;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(ClientName);

    /// <summary>
    /// Clé d'objet : « [préfixe global/]racine-d-usage/{guid}/{nom nettoyé} ».
    ///
    /// LE GUID N'EST PAS DÉCORATIF. Il rend la clé unique, donc l'objet immuable sous
    /// sa clé : deux vendeurs qui téléversent « photo.jpg » ne s'écrasent pas, et le
    /// contenu servi par une URL ne change jamais — ce qui autorise un cache public
    /// de très longue durée en tête du bucket.
    /// </summary>
    private string BuildObjectKey(string keyRoot, string fileName)
    {
        var safe = new string((fileName ?? "image").Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "image";
        }

        var prefix = _options.KeyPrefix.Trim().Trim('/');
        var baseKey = $"{keyRoot}/{Guid.NewGuid():N}/{safe}";
        return string.IsNullOrEmpty(prefix) ? baseKey : $"{prefix}/{baseKey}";
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UNE CLÉ EST UNE CLÉ. PLUS AUCUNE URL N'EST ACCEPTÉE ICI.
    ///
    /// Cette méthode acceptait aussi bien une clé qu'une URL publique, dont elle
    /// extrayait le chemin. Comme l'identifiant venait, sur un chemin, du CLIENT, et
    /// que les URL des images produit sont publiques, un vendeur pouvait désigner
    /// l'objet d'un concurrent et le faire supprimer.
    ///
    /// La cause première est corrigée en amont — plus aucune clé n'entre par une
    /// requête. Ce durcissement est le second rideau : le jour où un appelant
    /// transmettra de nouveau une chaîne mal contrôlée, elle sera refusée ici plutôt
    /// que résolue en objet d'autrui.
    ///
    /// La remontée de chemin est refusée pour la même raison : `products/../kyb/…`
    /// est une clé valide pour S3, qui ne normalise pas les segments. Elle l'est
    /// d'autant plus depuis qu'il existe PLUSIEURS buckets : une remontée permettrait
    /// de viser une racine d'usage qui n'est pas la sienne.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static string? NormalizeKey(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        var key = externalId.Trim().TrimStart('/');

        if (key.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || key.Contains("://", StringComparison.Ordinal)
            || key.Split('/').Any(segment => segment is ".." or "."))
        {
            return null;
        }

        return key.Length == 0 ? null : key;
    }

    private static string EncodeKey(string key)
        => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private static byte[] HmacSha256(byte[] key, string data)
        => new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));

    private static byte[] GetSignatureKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
