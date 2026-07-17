using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Traitement d'image via Cloudinary : upload signé de l'original, application du
/// détourage IA (<c>e_background_removal</c>) aplati sur fond blanc (<c>b_white</c>,
/// livré en JPEG), attente du rendu asynchrone (Cloudinary renvoie HTTP 423 tant
/// que le traitement est en cours), puis récupération des octets traités. L'asset
/// Cloudinary est ensuite détruit : Cloudinary ne sert qu'au traitement, l'image
/// finale étant stockée dans Cloudflare R2 par le flux de création de produit.
/// </summary>
public sealed class CloudinaryImageProcessor : IImageProcessor
{
    public const string ClientName = "cloudinary-image-processor";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<CloudinaryImageProcessor> _logger;

    public CloudinaryImageProcessor(
        IHttpClientFactory httpClientFactory, CloudinaryOptions options, ILogger<CloudinaryImageProcessor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ClientName);

            // 1) Upload signé de l'original vers Cloudinary.
            var (publicId, version, uploadError) = await UploadAsync(client, content, fileName, cancellationToken);
            if (string.IsNullOrEmpty(publicId))
            {
                return Error.Failure("image.process.upload_failed",
                    $"Cloudinary : upload de l'original échoué — {uploadError}");
            }

            // 2) Récupère la version détourée + fond blanc (JPEG). Le rendu IA est
            //    asynchrone : Cloudinary renvoie 423 (Locked) tant qu'il n'est pas prêt.
            var url = $"https://res.cloudinary.com/{_options.CloudName}/image/upload/" +
                      $"e_background_removal,b_white/v{version}/{publicId}.jpg";

            byte[]? processed = null;
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, _options.MaxWaitSeconds));
            while (DateTime.UtcNow < deadline)
            {
                using var resp = await client.GetAsync(url, cancellationToken);
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    processed = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    break;
                }
                if ((int)resp.StatusCode == 423) // Locked : traitement en cours → on patiente.
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Cloudinary : rendu détourage échoué ({Code}) sur {Url} : {Body}",
                    (int)resp.StatusCode, url, Trim(body));
                _ = DestroyAsync(client, publicId);
                return Error.Failure("image.process.render_failed",
                    $"Cloudinary : rendu détourage échoué ({(int)resp.StatusCode}) : {Trim(body)}");
            }

            // 3) Nettoyage best-effort de l'asset Cloudinary (stockage final = R2).
            _ = DestroyAsync(client, publicId);

            if (processed is null || processed.Length == 0)
            {
                return Error.Failure("image.process.timeout", "Cloudinary : délai de traitement dépassé.");
            }

            return new ProcessedImage(processed, "image/jpeg");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudinary : traitement d'image en erreur.");
            return Error.Failure("image.process.error", $"Cloudinary : {ex.Message}");
        }
    }

    private async Task<(string? publicId, long version, string? error)> UploadAsync(
        HttpClient client, byte[] content, string fileName, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Sign($"timestamp={timestamp}");

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { file, "file", string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName },
        };

        // Auth passée en QUERY-STRING : les champs multipart texte n'étaient pas
        // reconnus par Cloudinary (upload traité comme « unsigned »). En query,
        // api_key/timestamp/signature sont lus de façon fiable.
        var url = $"https://api.cloudinary.com/v1_1/{_options.CloudName}/image/upload" +
                  $"?api_key={Uri.EscapeDataString(_options.ApiKey)}" +
                  $"&timestamp={Uri.EscapeDataString(timestamp)}" +
                  $"&signature={Uri.EscapeDataString(signature)}";
        using var resp = await client.PostAsync(url, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cloudinary : upload échoué ({Code}) : {Body}", (int)resp.StatusCode, Trim(json));
            return (null, 0, $"HTTP {(int)resp.StatusCode} : {Trim(json)}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var publicId = root.TryGetProperty("public_id", out var p) ? p.GetString() : null;
        var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        return (publicId, version, null);
    }

    /// <summary>Supprime l'asset Cloudinary (best-effort : les erreurs sont ignorées).</summary>
    private async Task DestroyAsync(HttpClient client, string publicId)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = Sign($"public_id={publicId}&timestamp={timestamp}");
            var url = $"https://api.cloudinary.com/v1_1/{_options.CloudName}/image/destroy" +
                      $"?public_id={Uri.EscapeDataString(publicId)}" +
                      $"&api_key={Uri.EscapeDataString(_options.ApiKey)}" +
                      $"&timestamp={Uri.EscapeDataString(timestamp)}" +
                      $"&signature={Uri.EscapeDataString(signature)}";
            using var empty = new ByteArrayContent(Array.Empty<byte>());
            using var _ = await client.PostAsync(url, empty, CancellationToken.None);
        }
        catch
        {
            // Nettoyage best-effort : on ignore toute erreur.
        }
    }

    /// <summary>Signature Cloudinary : SHA1 hex de « params triés + api_secret ».</summary>
    private string Sign(string paramsToSign)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(paramsToSign + _options.ApiSecret));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
