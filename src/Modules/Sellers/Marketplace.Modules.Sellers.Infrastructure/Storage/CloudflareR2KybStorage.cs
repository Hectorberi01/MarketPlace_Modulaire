using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Application.Abstractions;

namespace Marketplace.Modules.Sellers.Infrastructure.Storage;

/// <summary>
/// Stockage RÉEL des pièces KYB sur Cloudflare R2 (API compatible S3). Signe
/// chaque PutObject en AWS Signature V4 (aucune dépendance SDK) et envoie le
/// fichier vers le bucket. Renvoie l'URL publique (domaine r2.dev / personnalisé).
/// </summary>
public sealed class CloudflareR2KybStorage : IKybDocumentStorage
{
    public const string ClientName = "cloudflare-r2";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareR2Options _options;

    public CloudflareR2KybStorage(IHttpClientFactory httpClientFactory, CloudflareR2Options options)
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
            var host = new Uri(_options.Endpoint).Host;

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
            var host = new Uri(_options.Endpoint).Host;
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
