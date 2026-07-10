using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Stockage RÉEL des images produit sur Cloudflare R2 (API compatible S3), signé
/// en AWS Signature V4 (sans SDK). Upload (PutObject) et suppression (DeleteObject)
/// pour respecter la contrainte : retirer une image d'un produit ou supprimer un
/// produit efface aussi le(s) fichier(s) sur R2. Les URLs renvoyées sont publiques.
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
            var key = BuildObjectKey(file.FileName);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            using var request = BuildSignedRequest(HttpMethod.Put, key, file.Content, contentType);
            request.Content = new ByteArrayContent(file.Content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var response = await CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<StoredMedia>(
                    Error.Failure("media.upload_failed", $"Cloudflare R2 — échec upload ({(int)response.StatusCode}) : {body}"));
            }

            var url = $"{_options.PublicBaseUrl.TrimEnd('/')}/{EncodeKey(key)}";
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

        try
        {
            var key = NormalizeKey(externalId);
            using var request = BuildSignedRequest(HttpMethod.Delete, key, Array.Empty<byte>(), contentType: null);

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

    private HttpRequestMessage BuildSignedRequest(HttpMethod method, string key, byte[] payload, string? contentType)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ");
        var dateStamp = now.ToString("yyyyMMdd");
        var host = new Uri(_options.Endpoint).Host;
        var payloadHash = Hex(SHA256.HashData(payload));
        var canonicalUri = "/" + _options.BucketName + "/" + EncodeKey(key);

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

        var requestUri = $"{_options.Endpoint}/{_options.BucketName}/{EncodeKey(key)}";
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        // content-type est un en-tête de contenu : à poser sur request.Content par l'appelant.
        // (déjà géré via MediaTypeHeaderValue dans UploadAsync)
        return request;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(ClientName);

    private string BuildObjectKey(string fileName)
    {
        var safe = new string((fileName ?? "image").Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "image";
        }

        var prefix = _options.KeyPrefix.Trim().Trim('/');
        var baseKey = $"products/{Guid.NewGuid():N}/{safe}";
        return string.IsNullOrEmpty(prefix) ? baseKey : $"{prefix}/{baseKey}";
    }

    // Accepte soit une clé (« products/… »), soit une URL publique dont on extrait la clé.
    private string NormalizeKey(string externalIdOrUrl)
    {
        if (!externalIdOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return externalIdOrUrl.TrimStart('/');
        }

        return new Uri(externalIdOrUrl).AbsolutePath.TrimStart('/');
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
