using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Application.Abstractions;

namespace Marketplace.Modules.Sellers.Infrastructure.Storage;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// STOCKAGE DES PIÈCES KYB SUR UN SERVICE S3-COMPATIBLE.
///
/// S'appelait `CloudflareR2KybStorage`, et le nom mentait déjà à moitié : rien ici
/// n'est propre à Cloudflare. Tout passe par AWS SigV4 en PATH-STYLE
/// (« /bucket/clé »), qui est le dénominateur commun de R2, MinIO, Ceph et S3.
///
/// Le seul verrou était l'endpoint, calculé depuis un identifiant de compte
/// Cloudflare. Il est désormais configurable (`ServiceUrl`), et cette classe vise
/// indifféremment R2 ou un MinIO auto-hébergé, sans une ligne de logique en plus.
///
/// PAS DE SDK AWS, ET C'EST UN CHOIX QUI SE PAIE ICI EN LISIBILITÉ. La signature
/// est écrite à la main plus bas. C'est une dépendance de moins et une surface de
/// moins, au prix d'un code qu'il faut relire avec attention : la moindre
/// différence entre l'URI canonique signée et l'URI réellement appelée produit un
/// « SignatureDoesNotMatch » qui ne dit pas d'où il vient.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class S3KybStorage : IKybDocumentStorage
{
    /// <summary>Nom du client HTTP nommé. Ne désigne plus un fournisseur : ce stockage peut être R2 ou MinIO.</summary>
    public const string ClientName = "kyb-object-storage";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly S3StorageOptions _options;

    public S3KybStorage(IHttpClientFactory httpClientFactory, S3StorageOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<Result<StoredKybFile>> UploadAsync(KybFileUpload file, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = BuildObjectKey(file.FileName);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            var now = DateTime.UtcNow;
            var amzDate = now.ToString("yyyyMMddTHHmmssZ");
            var dateStamp = now.ToString("yyyyMMdd");
            // ═══════════════════════════════════════════════════════════════════════
            // `Authority` ET NON `Host` — LE PORT FAIT PARTIE DE LA SIGNATURE.
            //
            // SigV4 signe l'en-tête `host` tel que le client l'enverra. Or
            // .NET écrit « 10.20.0.2:9000 » dans cet en-tête dès que le port
            // n'est pas celui du schéma, alors que `Uri.Host` rend
            // « 10.20.0.2 » tout court. Signer sans le port donnait donc une
            // signature portant sur un en-tête différent de celui envoyé :
            // « SignatureDoesNotMatch », sans indication de la cause.
            //
            // Invisible avec R2 (https sur 443, donc `Authority` == `Host`).
            // Systématique avec un MinIO sur 9000.
            // ═══════════════════════════════════════════════════════════════════════
            var host = new Uri(_options.Endpoint).Authority;

            // Chemin canonique = /bucket/clé (chaque segment de clé encodé, « / » conservés).
            var canonicalUri = "/" + _options.BucketName + "/" + EncodeKey(key);
            var payloadHash = Hex(SHA256.HashData(file.Content));

            const string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
            var canonicalHeaders =
                $"content-type:{contentType}\n" +
                $"host:{host}\n" +
                $"x-amz-content-sha256:{payloadHash}\n" +
                $"x-amz-date:{amzDate}\n";

            var canonicalRequest = $"PUT\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
            var scope = $"{dateStamp}/{_options.Region}/s3/aws4_request";
            var stringToSign =
                "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

            var signingKey = GetSignatureKey(_options.SecretAccessKey, dateStamp, _options.Region, "s3");
            var signature = Hex(HmacSha256(signingKey, stringToSign));

            var authorization =
                $"AWS4-HMAC-SHA256 Credential={_options.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";

            var requestUri = $"{_options.Endpoint}/{_options.BucketName}/{EncodeKey(key)}";
            using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
            {
                Content = new ByteArrayContent(file.Content)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            var client = _httpClientFactory.CreateClient(ClientName);
            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<StoredKybFile>(
                    Error.Failure("kyb.storage_failed", $"Cloudflare R2 — échec upload ({(int)response.StatusCode}) : {body}"));
            }

            var url = BuildPublicUrl(key);
            return new StoredKybFile(url, key);
        }
        catch (Exception ex)
        {
            return Result.Failure<StoredKybFile>(Error.Failure("kyb.storage_error", $"Cloudflare R2 : {ex.Message}"));
        }
    }

    public Result<string> CreatePresignedGetUrl(string objectKey, int expiresSeconds = 300)
    {
        try
        {
            var key = NormalizeKey(objectKey);
            var now = DateTime.UtcNow;
            var amzDate = now.ToString("yyyyMMddTHHmmssZ");
            var dateStamp = now.ToString("yyyyMMdd");
            // ═══════════════════════════════════════════════════════════════════════
            // `Authority` ET NON `Host` — LE PORT FAIT PARTIE DE LA SIGNATURE.
            //
            // SigV4 signe l'en-tête `host` tel que le client l'enverra. Or
            // .NET écrit « 10.20.0.2:9000 » dans cet en-tête dès que le port
            // n'est pas celui du schéma, alors que `Uri.Host` rend
            // « 10.20.0.2 » tout court. Signer sans le port donnait donc une
            // signature portant sur un en-tête différent de celui envoyé :
            // « SignatureDoesNotMatch », sans indication de la cause.
            //
            // Invisible avec R2 (https sur 443, donc `Authority` == `Host`).
            // Systématique avec un MinIO sur 9000.
            // ═══════════════════════════════════════════════════════════════════════
            var host = new Uri(_options.Endpoint).Authority;
            var scope = $"{dateStamp}/{_options.Region}/s3/aws4_request";

            // Paramètres de requête présignés (triés par clé, valeurs encodées RFC 3986).
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256",
                ["X-Amz-Credential"] = $"{_options.AccessKeyId}/{scope}",
                ["X-Amz-Date"] = amzDate,
                ["X-Amz-Expires"] = expiresSeconds.ToString(),
                ["X-Amz-SignedHeaders"] = "host"
            };
            var canonicalQuery = string.Join('&',
                query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            var canonicalUri = "/" + _options.BucketName + "/" + EncodeKey(key);
            var canonicalRequest =
                $"GET\n{canonicalUri}\n{canonicalQuery}\nhost:{host}\n\nhost\nUNSIGNED-PAYLOAD";

            var stringToSign =
                "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
                Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

            var signingKey = GetSignatureKey(_options.SecretAccessKey, dateStamp, _options.Region, "s3");
            var signature = Hex(HmacSha256(signingKey, stringToSign));

            var url = $"{_options.Endpoint}/{_options.BucketName}/{EncodeKey(key)}?{canonicalQuery}&X-Amz-Signature={signature}";
            return Result.Success(url);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.Failure("kyb.presign_error", $"Cloudflare R2 : {ex.Message}"));
        }
    }

    // Accepte soit une clé (« kyb/… »), soit une URL R2 complète dont on extrait la clé.
    private string NormalizeKey(string objectKeyOrUrl)
    {
        if (!objectKeyOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return objectKeyOrUrl.TrimStart('/');
        }

        var path = new Uri(objectKeyOrUrl).AbsolutePath.TrimStart('/');
        var prefix = _options.BucketName + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }

    private static string BuildObjectKey(string fileName)
    {
        var safe = new string((fileName ?? "document").Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "document";
        }

        return $"kyb/{Guid.NewGuid():N}/{safe}";
    }

    private string BuildPublicUrl(string key)
        => string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{_options.Endpoint}/{_options.BucketName}/{EncodeKey(key)}"
            : $"{_options.PublicBaseUrl.TrimEnd('/')}/{EncodeKey(key)}";

    // Encode chaque segment (RFC 3986) en conservant les séparateurs « / ».
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

    private static string Hex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();
}
